using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Hubs;

namespace MusterHubCommand.Api.Services;

// "No station found with code '…'" -- a validation failure, not a server
// error, whichever caller triggers it (Vision's own push, or an operator's
// manual form).
public class IncidentValidationException(string message) : Exception(message);

// One code path for both callers named in the plan: Vision's push/update/
// delete API and the control-room web console's own manual forms. Takes
// OrganisationId as an explicit parameter throughout rather than reading it
// off ICurrentOrganisationAccessor, since IntegrationIncidentsController is
// [AllowAnonymous] -- there is no ambient organisation until the caller's
// API key has already resolved one.
public class IncidentService(ApplicationDbContext db, CoreNotificationService notificationService, IHubContext<IncidentHub> hubContext)
{
    // Single chokepoint for the SignalR "something changed" nudge -- every
    // mutating method below calls this right after SaveChangesAsync,
    // instead of each controller action doing it individually. Sends only
    // an id, never a duplicated DTO -- see IncidentHub's own comment. Both
    // groups get it: the org-wide one for the incidents list, the
    // incident-specific one for anyone with that detail page open.
    private Task NotifyIncidentChangedAsync(Guid organisationId, Guid incidentId, CancellationToken ct = default) =>
        Task.WhenAll(
            hubContext.Clients.Group(IncidentHub.OrgGroup(organisationId)).SendAsync("IncidentUpdated", incidentId, ct),
            hubContext.Clients.Group(IncidentHub.IncidentGroup(incidentId)).SendAsync("IncidentUpdated", incidentId, ct));

    public Task<Incident?> FindByExternalReferenceAsync(Guid organisationId, string externalReference, CancellationToken ct = default) =>
        Query(organisationId).FirstOrDefaultAsync(i => i.ExternalReference == externalReference, ct);

    public Task<Incident?> FindByIdAsync(Guid organisationId, Guid id, CancellationToken ct = default) =>
        Query(organisationId).FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<List<Incident>> ListAsync(Guid organisationId, Guid? orgUnitId = null, bool activeOnly = false, CancellationToken ct = default) =>
        Query(organisationId)
            .Where(i => !activeOnly || i.Status == IncidentStatus.Open)
            .Where(i => orgUnitId == null || i.OrgUnitId == orgUnitId)
            .OrderByDescending(i => i.StartedAtUtc)
            .ToListAsync(ct);

    // AsSplitQuery -- without it, EF Core's default joins every one of
    // these sibling collections into a single SQL statement, and the row
    // count multiplies across all of them (appliances x updates x sectors
    // x objectives x actions x attachments x ...). A load test against a
    // real dev incident with 103 appliances, 36 updates, 4 sectors, 3
    // actions and 8 attachments produced a single query over 350,000 rows
    // and timed out outright -- on every GET, i.e. on every ~15s poll from
    // every device. Split queries instead: one query per collection,
    // joined only within its own ThenInclude chain, so the cost scales
    // with the sum of the collections' sizes, not their product.
    private IQueryable<Incident> Query(Guid organisationId) =>
        db.Incidents.IgnoreQueryFilters()
            .AsSplitQuery()
            .Include(i => i.Appliances).ThenInclude(a => a.OfficerInChargeEmployee)
            .Include(i => i.Updates)
            .Include(i => i.OrgUnit)
            .Include(i => i.CloseType)
            .Include(i => i.Sectors).ThenInclude(s => s.PersonInChargeEmployee)
            .Include(i => i.Objectives)
            .Include(i => i.Risks)
            .Include(i => i.BaEntryControlPoints).ThenInclude(p => p.Teams).ThenInclude(t => t.Wearers)
            .Include(i => i.Actions).ThenInclude(a => a.AssignedToEmployee)
            .Include(i => i.Attachments).ThenInclude(a => a.UploadedByEmployee)
            .Include(i => i.Attachments).ThenInclude(a => a.UploadedByDevice)
            .Where(i => i.OrganisationId == organisationId);

    public async Task<Incident> CreateOrUpsertAsync(Guid organisationId, CreateIncidentRequest request, CancellationToken ct = default)
    {
        var stationId = await ResolveStationIdAsync(organisationId, request.StationCode, ct)
            ?? throw new IncidentValidationException($"No station found with code '{request.StationCode}'.");

        var existing = await Query(organisationId).FirstOrDefaultAsync(i => i.ExternalReference == request.ExternalReference, ct);
        if (existing is not null)
        {
            existing.IncidentType = request.IncidentType;
            existing.Description = request.Description;
            existing.Address = request.Address;
            existing.Latitude = request.Latitude;
            existing.Longitude = request.Longitude;
            existing.OrgUnitId = stationId;
            if (request.StartedAtUtc is { } started) existing.StartedAtUtc = started;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await NotifyIncidentChangedAsync(organisationId, existing.Id, ct);
            existing.OrgUnit = await LoadOrgUnitAsync(stationId, ct);
            return existing;
        }

        var incident = new Incident
        {
            OrganisationId = organisationId,
            ExternalReference = request.ExternalReference,
            IncidentType = request.IncidentType,
            Description = request.Description,
            Address = request.Address,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            OrgUnitId = stationId,
            StartedAtUtc = request.StartedAtUtc ?? DateTimeOffset.UtcNow,
        };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incident.Id, ct);
        incident.OrgUnit = await LoadOrgUnitAsync(stationId, ct);

        // Only ever fires for a genuinely new incident, never the upsert-
        // existing branch above -- re-pushing an update from Vision (a
        // status change, an amended address) must not re-page the whole
        // crew's phones every time.
        await notificationService.NotifyStationEmployeesAsync(
            organisationId, stationId, $"New incident: {incident.IncidentType}", incident.Address ?? incident.OrgUnit?.Name ?? "");

        return incident;
    }

    public async Task<Incident?> PatchAsync(Guid organisationId, Guid incidentId, UpdateIncidentRequest request, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        if (request.IncidentType is not null) incident.IncidentType = request.IncidentType;
        if (request.Description is not null) incident.Description = request.Description;
        if (request.Address is not null) incident.Address = request.Address;
        if (request.Latitude is not null) incident.Latitude = request.Latitude;
        if (request.Longitude is not null) incident.Longitude = request.Longitude;
        if (request.StationCode is not null)
        {
            incident.OrgUnitId = await ResolveStationIdAsync(organisationId, request.StationCode, ct)
                ?? throw new IncidentValidationException($"No station found with code '{request.StationCode}'.");
        }
        if (request.Status is { } status)
        {
            incident.Status = status;
            incident.ClosedAtUtc = status == IncidentStatus.Open ? null : incident.ClosedAtUtc ?? DateTimeOffset.UtcNow;
        }
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        if (request.StationCode is not null) incident.OrgUnit = await LoadOrgUnitAsync(incident.OrgUnitId, ct);
        return incident;
    }

    public async Task<Incident?> CloseAsync(Guid organisationId, Guid incidentId, CloseIncidentRequest request, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        if (request.CloseTypeId is { } closeTypeId)
        {
            var closeTypeExists = await db.IncidentCloseTypes.IgnoreQueryFilters()
                .AnyAsync(t => t.Id == closeTypeId && t.OrganisationId == organisationId, ct);
            if (!closeTypeExists) throw new IncidentValidationException($"No close type found with id '{closeTypeId}'.");
        }

        incident.Status = IncidentStatus.Closed;
        incident.ClosedAtUtc ??= DateTimeOffset.UtcNow;
        incident.CloseTypeId = request.CloseTypeId;
        incident.CloseActionsTaken = request.ActionsTaken;
        incident.CloseOutcome = request.Outcome;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        if (request.CloseTypeId is not null) await db.Entry(incident).Reference(i => i.CloseType).LoadAsync(ct);
        return incident;
    }

    // Soft: marks Cancelled rather than removing the row, so a tablet with
    // the incident open shows "cancelled" instead of it vanishing.
    public async Task<Incident?> CancelAsync(Guid organisationId, Guid incidentId, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        incident.Status = IncidentStatus.Cancelled;
        incident.ClosedAtUtc ??= DateTimeOffset.UtcNow;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    // Replace-whole, not incremental -- same pattern as Rota's
    // SetAvailabilityContract -- so there's never drift between what Vision
    // thinks is attending and what the tablet shows after a make-up or
    // stand-down.
    public async Task<Incident?> SetAppliancesAsync(Guid organisationId, Guid incidentId, SetAppliancesRequest request, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        // Snapshotted before RemoveRange below so the timeline only gets an
        // entry for what actually changed -- Vision resyncing the same
        // attendance it already pushed must not spam one update per
        // appliance every time.
        var previousStatus = incident.Appliances.ToDictionary(a => a.Callsign, a => a.Status, StringComparer.OrdinalIgnoreCase);

        // SectorId/ResourceKind are Control-set metadata Vision knows
        // nothing about -- also snapshotted before RemoveRange and carried
        // forward by callsign, or a routine resync would silently wipe
        // whatever sector Control had organised.
        var previousSector = incident.Appliances.ToDictionary(a => a.Callsign, a => a.SectorId, StringComparer.OrdinalIgnoreCase);
        var previousResourceKind = incident.Appliances.ToDictionary(a => a.Callsign, a => a.ResourceKind, StringComparer.OrdinalIgnoreCase);
        var previousOfficerId = incident.Appliances.ToDictionary(a => a.Callsign, a => a.OfficerInChargeEmployeeId, StringComparer.OrdinalIgnoreCase);
        var previousOfficerName = incident.Appliances.ToDictionary(a => a.Callsign, a => a.OfficerInChargeName, StringComparer.OrdinalIgnoreCase);
        var previousOfficerEmployee = incident.Appliances.ToDictionary(a => a.Callsign, a => a.OfficerInChargeEmployee, StringComparer.OrdinalIgnoreCase);

        db.IncidentAppliances.RemoveRange(incident.Appliances);
        // AddRange explicitly, not just an assignment to the navigation --
        // IncidentAppliance.Id is client-generated and already non-default
        // by the time SaveChanges' graph-fixup would see it, so implicit
        // tracking reads it as Modified rather than Added and emits an
        // UPDATE against a row that doesn't exist yet, throwing a
        // DbUpdateConcurrencyException. Found via live verification.
        var newAppliances = request.Appliances
            .Select(a => new IncidentAppliance
            {
                IncidentId = incident.Id,
                Callsign = a.Callsign,
                Status = a.Status,
                SectorId = previousSector.GetValueOrDefault(a.Callsign),
                ResourceKind = previousResourceKind.GetValueOrDefault(a.Callsign),
                OfficerInChargeEmployeeId = previousOfficerId.GetValueOrDefault(a.Callsign),
                OfficerInChargeName = previousOfficerName.GetValueOrDefault(a.Callsign),
                OfficerInChargeEmployee = previousOfficerEmployee.GetValueOrDefault(a.Callsign),
            })
            .ToList();
        db.IncidentAppliances.AddRange(newAppliances);
        incident.Appliances = newAppliances;

        foreach (var appliance in newAppliances)
        {
            if (!previousStatus.TryGetValue(appliance.Callsign, out var priorStatus) || priorStatus != appliance.Status)
                QueueResourceChangeUpdate(incident, IncidentUpdateSource.ControlRoom, $"{appliance.Callsign} {appliance.Status}");
        }
        var newCallsigns = newAppliances.Select(a => a.Callsign).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var callsign in previousStatus.Keys.Where(c => !newCallsigns.Contains(c)))
            QueueResourceChangeUpdate(incident, IncidentUpdateSource.ControlRoom, $"{callsign} removed from attendance");

        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    // Upsert-one, deliberately not SetAppliancesAsync's replace-whole
    // semantics -- that's for Control/Vision's full push; a single tablet
    // reporting its own status (e.g. "Start navigation" -> EnRoute) must
    // never clobber every other appliance's attendance row. Case-
    // insensitive match on Callsign since it's operator-typed in two
    // different places (Setup's Device.Callsign, and whatever Vision/
    // Control originally pushed) and casing drift shouldn't fork a device
    // into a second, orphaned attendance row.
    public async Task<Incident?> SetSingleApplianceStatusAsync(
        Guid organisationId, Guid incidentId, string callsign, ApplianceStatus status, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var existing = incident.Appliances.FirstOrDefault(a => string.Equals(a.Callsign, callsign, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            // Only a genuine transition earns a timeline entry -- the
            // geofence check re-runs on every location report and would
            // otherwise re-log the same already-current status repeatedly.
            if (existing.Status != status)
            {
                existing.Status = status;
                existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
                QueueResourceChangeUpdate(incident, IncidentUpdateSource.Crew, $"{callsign} {status}");
            }
        }
        else
        {
            // Added explicitly via AddRange rather than relying on the
            // Appliances navigation's own change tracking -- see
            // SetAppliancesAsync's own comment on this same gotcha
            // (client-generated Id reads as Modified, not Added).
            var appliance = new IncidentAppliance { IncidentId = incident.Id, Callsign = callsign, Status = status };
            db.IncidentAppliances.Add(appliance);
            incident.Appliances.Add(appliance);
            QueueResourceChangeUpdate(incident, IncidentUpdateSource.Crew, $"{callsign} {status}");
        }
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    public async Task<Incident?> AddSectorAsync(
        Guid organisationId, Guid incidentId, string name,
        Guid? parentId = null, Guid? personInChargeEmployeeId = null, string? personInChargeName = null,
        CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        if (parentId is not null && incident.Sectors.All(s => s.Id != parentId))
            throw new IncidentValidationException("That parent doesn't belong to this incident.");

        var sector = new IncidentSector
        {
            IncidentId = incident.Id,
            Name = name,
            SortOrder = incident.Sectors.Count(s => s.ParentId == parentId),
            ParentId = parentId,
            // An employee link wins over free text, same dual-mode rule
            // UpdateSectorAsync applies -- never store both for one node.
            PersonInChargeEmployeeId = personInChargeEmployeeId,
            PersonInChargeName = personInChargeEmployeeId is null ? personInChargeName : null,
        };
        // A brand-new IncidentSector has no navigation properties EF can
        // fix up on its own -- FindAsync loads (or reuses an already-
        // tracked) Employee so ToDto's PersonInChargeEmployee?.DisplayName
        // resolves immediately, not just on the next fresh GET. Found via
        // live test failure: PersonInChargeName came back null right after
        // linking a real employee.
        if (personInChargeEmployeeId is not null)
            sector.PersonInChargeEmployee = await db.Employees.FindAsync([personInChargeEmployeeId], ct);
        // Only adds to db.IncidentSectors, not also to incident.Sectors --
        // same double-count gotcha QueueResourceChangeUpdate's own comment
        // documents: EF's change-tracker fixup already links a newly-Added
        // entity into an already-tracked principal's navigation collection
        // once its FK is set, so adding it here too listed the new sector
        // twice in the response. Found the same way that one was: a live
        // test failure (Sectors came back with two identical entries).
        db.IncidentSectors.Add(sector);
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    public async Task<Incident?> UpdateSectorAsync(
        Guid organisationId, Guid incidentId, Guid sectorId, string name,
        Guid? parentId, Guid? personInChargeEmployeeId, string? personInChargeName,
        CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var sector = incident.Sectors.FirstOrDefault(s => s.Id == sectorId);
        if (sector is null) return incident;

        if (parentId is not null)
        {
            if (parentId == sectorId)
                throw new IncidentValidationException("A node can't be its own parent.");
            if (incident.Sectors.All(s => s.Id != parentId))
                throw new IncidentValidationException("That parent doesn't belong to this incident.");
            // Re-parenting under one of the node's own descendants would
            // create a cycle nothing above it could ever reach again.
            if (IsDescendant(incident.Sectors, parentId.Value, sectorId))
                throw new IncidentValidationException("Can't move a node under its own descendant.");
        }

        sector.Name = name;
        sector.ParentId = parentId;
        sector.PersonInChargeEmployeeId = personInChargeEmployeeId;
        sector.PersonInChargeName = personInChargeEmployeeId is null ? personInChargeName : null;
        // Assigning the FK scalar doesn't refresh the navigation EF already
        // loaded for the OLD value -- same fix AddSectorAsync needs, see
        // its own comment.
        sector.PersonInChargeEmployee = personInChargeEmployeeId is null
            ? null
            : await db.Employees.FindAsync([personInChargeEmployeeId], ct);
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    // True when candidateAncestorId is nodeId itself or one of its
    // ancestors is nodeId -- i.e. would moving nodeId under
    // candidateAncestorId create a cycle. Walks up candidateAncestorId's
    // own parent chain rather than down nodeId's descendants, since the
    // chain to the root is normally far shorter than a whole sub-tree.
    private static bool IsDescendant(List<IncidentSector> allSectors, Guid candidateAncestorId, Guid nodeId)
    {
        var current = allSectors.FirstOrDefault(s => s.Id == candidateAncestorId);
        while (current is not null)
        {
            if (current.Id == nodeId) return true;
            current = current.ParentId is null ? null : allSectors.FirstOrDefault(s => s.Id == current.ParentId);
        }
        return false;
    }

    // Removes the whole sub-tree, not just this one node -- a node's
    // descendants don't mean anything without it. The database's own
    // Cascade FK (see ApplicationDbContext) is a safety net, not relied on
    // here: computing the exact set in-memory means the Incident this
    // method returns already reflects the removal, not just a fresh
    // re-query after the fact. Appliances attached anywhere in the removed
    // sub-tree fall back to Unassigned via the separate SetNull FK -- not
    // touched here, EF's own change tracking clears SectorId on already-
    // loaded IncidentAppliance rows when their sector is removed in the
    // same SaveChanges call.
    public async Task<Incident?> DeleteSectorAsync(Guid organisationId, Guid incidentId, Guid sectorId, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var sector = incident.Sectors.FirstOrDefault(s => s.Id == sectorId);
        if (sector is null) return incident;

        var toRemove = DescendantsAndSelf(incident.Sectors, sectorId);
        db.IncidentSectors.RemoveRange(toRemove);
        foreach (var removed in toRemove) incident.Sectors.Remove(removed);

        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    private static List<IncidentSector> DescendantsAndSelf(List<IncidentSector> allSectors, Guid rootId)
    {
        var result = new List<IncidentSector>();
        void Collect(Guid id)
        {
            var node = allSectors.FirstOrDefault(s => s.Id == id);
            if (node is null) return;
            result.Add(node);
            foreach (var child in allSectors.Where(s => s.ParentId == id))
                Collect(child.Id);
        }
        Collect(rootId);
        return result;
    }

    // Null sectorId un-assigns (back to "Unassigned") -- not a delete of
    // anything, just clearing the appliance's own FK.
    public async Task<Incident?> AssignApplianceSectorAsync(Guid organisationId, Guid incidentId, Guid applianceId, Guid? sectorId, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var appliance = incident.Appliances.FirstOrDefault(a => a.Id == applianceId);
        if (appliance is null) return incident;

        if (sectorId is not null && incident.Sectors.All(s => s.Id != sectorId))
            throw new IncidentValidationException("That sector doesn't belong to this incident.");

        appliance.SectorId = sectorId;
        appliance.UpdatedAtUtc = DateTimeOffset.UtcNow;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    public async Task<Incident?> SetApplianceResourceKindAsync(Guid organisationId, Guid incidentId, Guid applianceId, ResourceKind resourceKind, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var appliance = incident.Appliances.FirstOrDefault(a => a.Id == applianceId);
        if (appliance is null) return incident;

        appliance.ResourceKind = resourceKind;
        appliance.UpdatedAtUtc = DateTimeOffset.UtcNow;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    // ID-keyed, unlike SetSingleApplianceStatusAsync's own callsign-keyed
    // self-report (used by the tablet's own "Start navigation" and geofence
    // flows) -- this is the control room picking a specific appliance row
    // to change inline, same shape as SetApplianceResourceKindAsync/
    // AssignApplianceSectorAsync right above. Still only logs a Timeline
    // entry on a genuine transition, same guard SetSingleApplianceStatusAsync
    // already applies.
    public async Task<Incident?> SetApplianceStatusAsync(Guid organisationId, Guid incidentId, Guid applianceId, ApplianceStatus status, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var appliance = incident.Appliances.FirstOrDefault(a => a.Id == applianceId);
        if (appliance is null) return incident;

        if (appliance.Status != status)
        {
            appliance.Status = status;
            appliance.UpdatedAtUtc = DateTimeOffset.UtcNow;
            QueueResourceChangeUpdate(incident, IncidentUpdateSource.ControlRoom, $"{appliance.Callsign} {status}");
            incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        }
        return incident;
    }

    public async Task<Incident?> SetApplianceOfficerAsync(
        Guid organisationId, Guid incidentId, Guid applianceId,
        Guid? officerInChargeEmployeeId, string? officerInChargeName, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var appliance = incident.Appliances.FirstOrDefault(a => a.Id == applianceId);
        if (appliance is null) return incident;

        appliance.OfficerInChargeEmployeeId = officerInChargeEmployeeId;
        appliance.OfficerInChargeName = officerInChargeEmployeeId is null ? officerInChargeName : null;
        // The FK scalar alone doesn't refresh the navigation -- EF's fixup
        // only wires it up against an entity already tracked in this same
        // DbContext, same gotcha already hit twice on IncidentSector's own
        // PersonInChargeEmployee.
        appliance.OfficerInChargeEmployee = officerInChargeEmployeeId is null
            ? null
            : await db.Employees.FindAsync([officerInChargeEmployeeId], ct);
        appliance.UpdatedAtUtc = DateTimeOffset.UtcNow;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    // Writes into the previously-reserved-but-unused ResourceChange slot
    // (IncidentUpdateType.ResourceChange) so a status change becomes a
    // real, permanent timeline entry -- IncidentAppliance itself only ever
    // stores CURRENT status, so this update row is the only place a
    // Mobilised -> EnRoute -> OnScene history survives. Text matches the
    // same bare enum string the status pills already render elsewhere
    // (e.g. "KV57P1 EnRoute"), not a separately-invented phrasing. Only
    // adds to db.IncidentUpdates, not also to incident.Updates -- EF's own
    // change-tracker fixup already links a newly-Added entity into an
    // already-tracked principal's navigation collection once its FK is
    // set, so adding it a second time here double-counted every entry in
    // the Incident this method returns. Found via live test failure: two
    // appliances dispatched together came back as four ResourceChange
    // updates, not two.
    private void QueueResourceChangeUpdate(Incident incident, IncidentUpdateSource source, string text)
    {
        db.IncidentUpdates.Add(new IncidentUpdate
        {
            IncidentId = incident.Id,
            Source = source,
            Text = text,
            UpdateType = IncidentUpdateType.ResourceChange,
        });
    }

    // Same shape as QueueResourceChangeUpdate -- an IncidentAction status
    // change becomes a real Timeline entry, so the Timeline stays the one
    // "what happened" feed instead of a second, disconnected log.
    private void QueueActionChangeUpdate(Incident incident, IncidentUpdateSource source, string text)
    {
        db.IncidentUpdates.Add(new IncidentUpdate
        {
            IncidentId = incident.Id,
            Source = source,
            Text = text,
            UpdateType = IncidentUpdateType.ActionChange,
        });
    }

    public async Task<Incident?> RaiseActionAsync(
        Guid organisationId, Guid incidentId, IncidentActionKind kind, string text, IncidentUpdateSource source,
        string? raisedByName, Guid? raisedByEmployeeId,
        Guid? assignedToEmployeeId, string? assignedToName, Guid? sectorId,
        CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        if (sectorId is not null && incident.Sectors.All(s => s.Id != sectorId))
            throw new IncidentValidationException("That sector doesn't belong to this incident.");

        var action = new IncidentAction
        {
            IncidentId = incident.Id,
            Kind = kind,
            Text = text,
            Source = source,
            RaisedByName = raisedByName,
            RaisedByEmployeeId = raisedByEmployeeId,
            AssignedToEmployeeId = assignedToEmployeeId,
            AssignedToName = assignedToEmployeeId is null ? assignedToName : null,
        };
        if (sectorId is not null) action.SectorId = sectorId;
        // Same navigation-fixup gotcha as AddSectorAsync's own comment --
        // a brand-new entity's FK scalar alone doesn't resolve the
        // navigation, so ToDto's AssignedToEmployee?.DisplayName would
        // otherwise come back null until the next fresh GET.
        if (assignedToEmployeeId is not null)
            action.AssignedToEmployee = await db.Employees.FindAsync([assignedToEmployeeId], ct);

        // Not also added to incident.Actions -- same EF change-tracker
        // fixup gotcha documented on QueueResourceChangeUpdate/AddSectorAsync.
        db.IncidentActions.Add(action);
        var label = kind == IncidentActionKind.Task ? "Task raised" : "Resource request";
        QueueActionChangeUpdate(incident, source, $"{label}: {text}");

        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    // Idempotent, same guard as AcknowledgeUpdateAsync.
    public async Task<Incident?> AcknowledgeActionAsync(Guid organisationId, Guid incidentId, Guid actionId, string acknowledgedByName, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var action = incident.Actions.FirstOrDefault(a => a.Id == actionId);
        if (action is null) return incident;

        if (action.AcknowledgedAtUtc is null)
        {
            action.AcknowledgedAtUtc = DateTimeOffset.UtcNow;
            action.AcknowledgedByName = acknowledgedByName;
            if (action.Status == IncidentActionStatus.Open) action.Status = IncidentActionStatus.Acknowledged;
            QueueActionChangeUpdate(incident, action.Source, $"{action.Text}: acknowledged by {acknowledgedByName}");
            incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        }
        return incident;
    }

    // Idempotent -- resolving an already-resolved action is a no-op, same
    // reasoning as AcknowledgeUpdateAsync (two crew members' tablets both
    // catching up on the same poll can't stomp on the first resolution).
    public async Task<Incident?> ResolveActionAsync(
        Guid organisationId, Guid incidentId, Guid actionId, IncidentActionStatus status, string resolvedByName, CancellationToken ct = default)
    {
        if (status != IncidentActionStatus.Completed && status != IncidentActionStatus.Declined)
            throw new IncidentValidationException("Status must be Completed or Declined.");

        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var action = incident.Actions.FirstOrDefault(a => a.Id == actionId);
        if (action is null) return incident;

        if (action.ResolvedAtUtc is null)
        {
            action.Status = status;
            action.ResolvedAtUtc = DateTimeOffset.UtcNow;
            action.ResolvedByName = resolvedByName;
            QueueActionChangeUpdate(incident, action.Source, $"{action.Text}: {status.ToString().ToLowerInvariant()} by {resolvedByName}");
            incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        }
        return incident;
    }

    // Same shape as QueueResourceChangeUpdate/QueueActionChangeUpdate -- a
    // newly-identified risk becomes a real Timeline entry (it's a genuine
    // event worth broadcasting), reusing the existing Hazard type rather
    // than adding a new one: Hazard is already acknowledgeable and already
    // renders with its own red dot/"HAZARD" caption in the web timeline, so
    // a risk-log entry gets that treatment for free. Only raising a risk
    // does this -- ControlRiskAsync/ReopenRiskAsync deliberately don't, see
    // their own comments, same reasoning as Objectives' Achieve/Reopen.
    private void QueueRiskRaisedUpdate(Incident incident, IncidentUpdateSource source, string text)
    {
        db.IncidentUpdates.Add(new IncidentUpdate
        {
            IncidentId = incident.Id,
            Source = source,
            Text = text,
            UpdateType = IncidentUpdateType.Hazard,
        });
    }

    public async Task<Incident?> RaiseRiskAsync(
        Guid organisationId, Guid incidentId, string description, IncidentRiskLevel riskLevel, string? controlMeasure,
        IncidentUpdateSource source, string raisedByName, Guid? raisedByEmployeeId, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        // Not also added to incident.Risks -- same EF change-tracker fixup
        // gotcha as RaiseObjectiveAsync/RaiseActionAsync.
        db.IncidentRisks.Add(new IncidentRisk
        {
            IncidentId = incident.Id,
            Description = description,
            RiskLevel = riskLevel,
            ControlMeasure = controlMeasure,
            Source = source,
            RaisedByName = raisedByName,
            RaisedByEmployeeId = raisedByEmployeeId,
        });
        QueueRiskRaisedUpdate(incident, source, $"Risk identified ({riskLevel}): {description}");

        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    // Idempotent, same "two tablets racing the same ~15s poll" reasoning as
    // AchieveObjectiveAsync. Deliberately no Timeline write here -- the
    // ongoing Identified->Controlled state lives in the Risk Log tab
    // itself, same reasoning Objectives already established for its own
    // Achieve.
    public async Task<Incident?> ControlRiskAsync(Guid organisationId, Guid incidentId, Guid riskId, string reviewedByName, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var risk = incident.Risks.FirstOrDefault(r => r.Id == riskId);
        if (risk is null) return incident;

        if (risk.Status != IncidentRiskStatus.Controlled)
        {
            risk.Status = IncidentRiskStatus.Controlled;
            risk.ReviewedAtUtc = DateTimeOffset.UtcNow;
            risk.ReviewedByName = reviewedByName;
            incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        }
        return incident;
    }

    // Same idempotency shape as ControlRiskAsync, the other direction.
    // Clears ReviewedAtUtc/ReviewedByName rather than leaving the prior
    // reviewer's stamp -- if this gets controlled again later that's a
    // fresh review, not a continuation of the old one. Same reasoning as
    // ReopenObjectiveAsync.
    public async Task<Incident?> ReopenRiskAsync(Guid organisationId, Guid incidentId, Guid riskId, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var risk = incident.Risks.FirstOrDefault(r => r.Id == riskId);
        if (risk is null) return incident;

        if (risk.Status != IncidentRiskStatus.Identified)
        {
            risk.Status = IncidentRiskStatus.Identified;
            risk.ReviewedAtUtc = null;
            risk.ReviewedByName = null;
            incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        }
        return incident;
    }

    // No QueueXChangeUpdate anywhere in these four -- same reasoning as
    // Objectives/Risks' own ongoing-state changes, a BA board is its own
    // place, not another Timeline feed. Org-gating (BaEntryControlEnabled)
    // happens at the controller, not here -- these methods don't need to
    // know why they were or weren't allowed to be called. Tablet-only by
    // convention (also enforced at the controller): the ECO is physically
    // at the point, not a remote control-room operator, so the web console
    // only ever reads this data, never writes it.
    // deviceId becomes the point's OwningDeviceId -- a tablet is physically
    // at one entry control point, not several, so the caller (see
    // TabletIncidentsController.AddBaEntryControlPoint) has already
    // rejected this if the device owns one already.
    public async Task<Incident?> AddBaEntryControlPointAsync(
        Guid organisationId, Guid incidentId, string name, BaStage stage, Guid deviceId, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        // Not also added to incident.BaEntryControlPoints -- same EF
        // change-tracker fixup gotcha as every other RaiseXAsync/AddXAsync
        // in this file.
        db.BaEntryControlPoints.Add(new BaEntryControlPoint { IncidentId = incident.Id, Name = name, Stage = stage, OwningDeviceId = deviceId });

        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    // Idempotent, same "two tablets racing the same poll" reasoning as
    // every other resolve-style method here. Only takes if the point is
    // currently unclaimed -- claiming one another device already owns, or
    // re-claiming one this device already owns, is a silent no-op rather
    // than an error.
    public async Task<Incident?> ClaimBaEntryControlPointAsync(
        Guid organisationId, Guid incidentId, Guid pointId, Guid deviceId, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var point = incident.BaEntryControlPoints.FirstOrDefault(p => p.Id == pointId);
        if (point is null) return incident;

        if (point.OwningDeviceId is null)
        {
            point.OwningDeviceId = deviceId;
            incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        }
        return incident;
    }

    // Only the owning device can release its own point -- a no-op if this
    // device doesn't currently own it (including if it's already
    // unclaimed).
    public async Task<Incident?> HandOverBaEntryControlPointAsync(
        Guid organisationId, Guid incidentId, Guid pointId, Guid deviceId, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var point = incident.BaEntryControlPoints.FirstOrDefault(p => p.Id == pointId);
        if (point is null) return incident;

        if (point.OwningDeviceId == deviceId)
        {
            point.OwningDeviceId = null;
            incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        }
        return incident;
    }

    public async Task<Incident?> AddBaTeamAsync(
        Guid organisationId, Guid incidentId, Guid pointId, string name, string teamLeader,
        string? commsChannel, string? briefing, string? equipment, Guid deviceId, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var point = incident.BaEntryControlPoints.FirstOrDefault(p => p.Id == pointId);
        if (point is null || point.OwningDeviceId != deviceId) return incident;

        db.BaTeams.Add(new BaTeam
        {
            EntryControlPointId = point.Id,
            Name = name,
            TeamLeader = teamLeader,
            CommsChannel = commsChannel,
            Briefing = briefing,
            Equipment = equipment,
        });

        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    // whistleMinutes, not a caller-supplied WhistleAtUtc -- resolved
    // against this server's own clock (like EnteredAtUtc's default
    // already is), not a tablet's, which isn't trustworthy enough to
    // compute a timestamp that gets persisted.
    public async Task<Incident?> AddBaWearerAsync(
        Guid organisationId, Guid incidentId, Guid teamId, string name, double cylinderPressureBar, int whistleMinutes,
        Guid deviceId, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var point = incident.BaEntryControlPoints.FirstOrDefault(p => p.Teams.Any(t => t.Id == teamId));
        var team = point?.Teams.FirstOrDefault(t => t.Id == teamId);
        if (team is null || point!.OwningDeviceId != deviceId) return incident;

        db.BaWearers.Add(new BaWearer
        {
            TeamId = team.Id,
            Name = name,
            CylinderPressureBar = cylinderPressureBar,
            WhistleAtUtc = DateTimeOffset.UtcNow.AddMinutes(whistleMinutes),
        });

        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    // Idempotent, same "two tablets racing the same poll" reasoning as
    // every other resolve-style method here -- but one-way, no Reopen: once
    // exited, that wearer's record is done (see BaWearer's own comment).
    public async Task<Incident?> ExitBaWearerAsync(Guid organisationId, Guid incidentId, Guid wearerId, Guid deviceId, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var point = incident.BaEntryControlPoints.FirstOrDefault(p => p.Teams.Any(t => t.Wearers.Any(w => w.Id == wearerId)));
        var wearer = point?.Teams.SelectMany(t => t.Wearers).FirstOrDefault(w => w.Id == wearerId);
        if (wearer is null || point!.OwningDeviceId != deviceId) return incident;

        if (wearer.ExitedAtUtc is null)
        {
            wearer.ExitedAtUtc = DateTimeOffset.UtcNow;
            incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        }
        return incident;
    }

    // Deliberately no QueueActionChangeUpdate/IncidentUpdate write anywhere
    // in these three methods -- objectives are a structured list distinct
    // from the Timeline feed, by design.
    public async Task<Incident?> RaiseObjectiveAsync(
        Guid organisationId, Guid incidentId, string text, IncidentUpdateSource source,
        string raisedByName, Guid? raisedByEmployeeId, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        // Not also added to incident.Objectives -- same EF change-tracker
        // fixup gotcha as RaiseActionAsync/AddSectorAsync. No nav-fixup
        // needed the way RaiseActionAsync needs for AssignedToEmployee --
        // RaisedByName is already the string to display, never resolved
        // live from a nav.
        db.IncidentObjectives.Add(new IncidentObjective
        {
            IncidentId = incident.Id,
            Text = text,
            Source = source,
            RaisedByName = raisedByName,
            RaisedByEmployeeId = raisedByEmployeeId,
        });

        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        return incident;
    }

    // Idempotent, same "two tablets racing the same ~15s poll" reasoning
    // as AcknowledgeActionAsync -- but guarded on Status equality, not a
    // nullable timestamp, since Achieved can later flip back to Open via
    // ReopenObjectiveAsync and be achieved again, unlike IncidentAction's
    // one-way ResolvedAtUtc.
    public async Task<Incident?> AchieveObjectiveAsync(Guid organisationId, Guid incidentId, Guid objectiveId, string achievedByName, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var objective = incident.Objectives.FirstOrDefault(o => o.Id == objectiveId);
        if (objective is null) return incident;

        if (objective.Status != IncidentObjectiveStatus.Achieved)
        {
            objective.Status = IncidentObjectiveStatus.Achieved;
            objective.AchievedAtUtc = DateTimeOffset.UtcNow;
            objective.AchievedByName = achievedByName;
            incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        }
        return incident;
    }

    // Same idempotency shape as AchieveObjectiveAsync, the other
    // direction. Clears AchievedAtUtc/AchievedByName rather than leaving
    // the prior achiever's stamp -- if this gets achieved again later
    // that's a fresh achievement, not a continuation of the old one.
    public async Task<Incident?> ReopenObjectiveAsync(Guid organisationId, Guid incidentId, Guid objectiveId, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var objective = incident.Objectives.FirstOrDefault(o => o.Id == objectiveId);
        if (objective is null) return incident;

        if (objective.Status != IncidentObjectiveStatus.Open)
        {
            objective.Status = IncidentObjectiveStatus.Open;
            objective.AchievedAtUtc = null;
            objective.AchievedByName = null;
            incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        }
        return incident;
    }

    // Append-only -- nothing here ever edits or removes a prior update.
    public async Task<Incident?> AddUpdateAsync(
        Guid organisationId, Guid incidentId, IncidentUpdateSource source,
        string? authorName, Guid? authorEmployeeId, string text, IncidentUpdateType updateType,
        Guid? replyToUpdateId = null, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        if (replyToUpdateId is not null && incident.Updates.All(u => u.Id != replyToUpdateId))
            throw new IncidentValidationException("That message doesn't belong to this incident.");

        db.IncidentUpdates.Add(new IncidentUpdate
        {
            IncidentId = incident.Id,
            Source = source,
            AuthorName = authorName,
            AuthorEmployeeId = authorEmployeeId,
            Text = text,
            UpdateType = updateType,
            ReplyToUpdateId = replyToUpdateId,
        });
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        await db.Entry(incident).Collection(i => i.Updates).LoadAsync(ct);
        return incident;
    }

    // Idempotent -- acknowledging an already-acknowledged update just
    // returns the incident unchanged rather than overwriting who/when, so
    // a second tap (or two crew members' tablets both catching up on the
    // same poll) can't stomp on the first acknowledgement.
    public async Task<Incident?> AcknowledgeUpdateAsync(Guid organisationId, Guid incidentId, Guid updateId, string acknowledgedByName, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var update = incident.Updates.FirstOrDefault(u => u.Id == updateId);
        if (update is null) return incident;

        if (update.AcknowledgedAtUtc is null)
        {
            update.AcknowledgedAtUtc = DateTimeOffset.UtcNow;
            update.AcknowledgedByName = acknowledgedByName;
            await db.SaveChangesAsync(ct);
            await NotifyIncidentChangedAsync(organisationId, incidentId, ct);
        }
        return incident;
    }

    // db.Entry(...).Reference(...).LoadAsync() applies the ambient tenant
    // filter, which resolves to Guid.Empty for IntegrationIncidentsController's
    // anonymous callers -- silently loading nothing rather than throwing, so
    // the bug shows up as a blank OrgUnitName, not an error. IgnoreQueryFilters
    // sidesteps it, matching the explicit organisationId this whole service
    // already threads through instead of relying on ambient state.
    private Task<OrgUnit?> LoadOrgUnitAsync(Guid orgUnitId, CancellationToken ct) =>
        db.OrgUnits.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == orgUnitId, ct);

    private Task<Guid?> ResolveStationIdAsync(Guid organisationId, string stationCode, CancellationToken ct) =>
        db.OrgUnits.IgnoreQueryFilters()
            .Where(u => u.OrganisationId == organisationId && u.Code == stationCode)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
}
