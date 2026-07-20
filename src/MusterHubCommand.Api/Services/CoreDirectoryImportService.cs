using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Services;

public record CoreDirectoryImportSummary(
    string ServiceName,
    int UnitsCreated,
    int UnitsUpdated,
    int EmployeesCreated,
    int EmployeesUpdated,
    int StationMembershipsChanged);

// One-way, idempotent import of core's directory into Command: Groups and
// Stations become OrgUnits (typed Group > Station, mirroring core's own
// shape), Users become Employees. Re-running is an upsert, never a
// duplicate -- units match on CoreId, employees on PersonId. Deliberately
// never deletes or detaches anything: someone removed in core may still
// have Command history worth keeping.
public class CoreDirectoryImportService(ApplicationDbContext db, ICoreDirectoryClient client)
{
    public async Task<CoreDirectoryImportSummary> ImportAsync(
        Guid organisationId, string apiKey, CancellationToken cancellationToken = default)
    {
        var directory = await client.FetchAsync(apiKey, cancellationToken);

        // The key must belong to the organisation the caller is signed into:
        // an admin holding a leaked key for a *different* service must not be
        // able to pull that service's workforce into their own tenant.
        if (directory.ServiceId != organisationId)
            throw new CoreDirectoryMismatchException();

        var groupType = await GetOrCreateTypeAsync(organisationId, "GRP", "Group", allowedParentTypeId: null, cancellationToken);
        var stationType = await GetOrCreateTypeAsync(organisationId, "STN", "Station", groupType.Id, cancellationToken);

        var units = await db.OrgUnits.ToListAsync(cancellationToken);
        int unitsCreated = 0, unitsUpdated = 0;

        OrgUnit Upsert(Guid coreId, string name, string? code, Guid typeId, Guid? parentId)
        {
            var unit = units.FirstOrDefault(u => u.CoreId == coreId)
                ?? units.FirstOrDefault(u => u.CoreId == null && u.OrgUnitTypeId == typeId && u.Name == name);
            if (unit is null)
            {
                unit = new OrgUnit
                {
                    OrganisationId = organisationId,
                    OrgUnitTypeId = typeId,
                    ParentId = parentId,
                    Name = name,
                    Code = code,
                    CoreId = coreId,
                };
                db.OrgUnits.Add(unit);
                units.Add(unit);
                unitsCreated++;
            }
            else
            {
                var changed = unit.Name != name || unit.Code != code || unit.CoreId != coreId;
                unit.Name = name;
                unit.Code = code;
                unit.CoreId = coreId;
                if (changed) unitsUpdated++;
            }
            return unit;
        }

        var groupUnitsByCoreId = new Dictionary<Guid, OrgUnit>();
        foreach (var group in directory.Groups)
            groupUnitsByCoreId[group.Id] = Upsert(group.Id, group.Name, code: null, groupType.Id, parentId: null);

        var stationUnitsByCoreId = new Dictionary<Guid, OrgUnit>();
        foreach (var station in directory.Stations)
        {
            var parent = groupUnitsByCoreId.GetValueOrDefault(station.GroupId);
            stationUnitsByCoreId[station.Id] = Upsert(station.Id, station.Name, station.Code, stationType.Id, parent?.Id);
        }

        // Parent-first save so OrgUnitPathInterceptor can resolve new
        // stations' paths against groups created in this same run.
        await db.SaveChangesAsync(cancellationToken);

        var employees = await db.Employees.ToListAsync(cancellationToken);
        var assignments = await db.EmployeeStationAssignments.ToListAsync(cancellationToken);
        int employeesCreated = 0, employeesUpdated = 0, membershipsChanged = 0;

        foreach (var user in directory.Users)
        {
            var employee = employees.FirstOrDefault(e => e.PersonId == user.Id);
            if (employee is null)
            {
                employee = new Employee
                {
                    OrganisationId = organisationId,
                    PersonId = user.Id,
                    DisplayName = user.FullName,
                    EmployeeNumber = user.EmployeeNumber,
                };
                db.Employees.Add(employee);
                employees.Add(employee);
                employeesCreated++;
            }
            else
            {
                var newNumber = user.EmployeeNumber ?? employee.EmployeeNumber;
                if (employee.DisplayName != user.FullName || employee.EmployeeNumber != newNumber)
                {
                    employee.DisplayName = user.FullName;
                    employee.EmployeeNumber = newNumber;
                    employeesUpdated++;
                }
            }

            // Replace-whole per employee, not date-ranged like Rota/Skills:
            // Command only ever needs "who is at this station right now" for
            // the new-incident push, so a membership core no longer reports
            // is removed outright rather than left to linger -- paging
            // someone at a station they've transferred away from is a real
            // operational cost, not just stale data.
            var wanted = user.Stations
                .Select(m => (Unit: stationUnitsByCoreId.GetValueOrDefault(m.StationId), m.IsHome))
                .Where(m => m.Unit is not null)
                .ToList();

            var current = assignments.Where(a => a.EmployeeId == employee.Id).ToList();
            foreach (var stale in current.Where(a => wanted.All(w => w.Unit!.Id != a.OrgUnitId)))
            {
                db.EmployeeStationAssignments.Remove(stale);
                assignments.Remove(stale);
                membershipsChanged++;
            }

            foreach (var (unit, isHome) in wanted)
            {
                var existing = current.FirstOrDefault(a => a.OrgUnitId == unit!.Id);
                if (existing is null)
                {
                    var assignment = new EmployeeStationAssignment
                    {
                        OrganisationId = organisationId,
                        EmployeeId = employee.Id,
                        OrgUnitId = unit!.Id,
                        IsHome = isHome,
                    };
                    db.EmployeeStationAssignments.Add(assignment);
                    assignments.Add(assignment);
                    membershipsChanged++;
                }
                else if (existing.IsHome != isHome)
                {
                    existing.IsHome = isHome;
                    membershipsChanged++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new CoreDirectoryImportSummary(
            directory.ServiceName,
            unitsCreated, unitsUpdated,
            employeesCreated, employeesUpdated,
            membershipsChanged);
    }

    private async Task<OrgUnitType> GetOrCreateTypeAsync(
        Guid organisationId, string code, string name, Guid? allowedParentTypeId, CancellationToken cancellationToken)
    {
        var type = await db.OrgUnitTypes.FirstOrDefaultAsync(t => t.Code == code, cancellationToken);
        if (type is not null) return type;

        type = new OrgUnitType { OrganisationId = organisationId, Code = code, Name = name, AllowedParentTypeId = allowedParentTypeId };
        db.OrgUnitTypes.Add(type);
        await db.SaveChangesAsync(cancellationToken);
        return type;
    }
}

public class CoreDirectoryMismatchException : Exception
{
    public CoreDirectoryMismatchException()
        : base("That API key belongs to a different MusterHub service than the one you're signed into.") { }
}
