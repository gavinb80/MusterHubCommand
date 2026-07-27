using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

[Route("api/employees")]
public class EmployeesController(
    ApplicationDbContext db,
    ICurrentOrganisationAccessor organisationAccessor,
    ICurrentEmployeeAccessor currentEmployeeAccessor,
    OperatorPermissionChecker operatorChecker)
    : CommandControllerBase(organisationAccessor, currentEmployeeAccessor, operatorChecker)
{
    [HttpGet]
    public async Task<ActionResult<List<EmployeeDto>>> List()
    {
        var tiersByEmployeeId = await db.CommandOperators.ToDictionaryAsync(o => o.EmployeeId, o => o.Tier);
        var employees = await db.Employees.OrderBy(e => e.DisplayName).ToListAsync();
        return Ok(employees.Select(e =>
            new EmployeeDto(e.Id, e.DisplayName, e.EmployeeNumber, tiersByEmployeeId.TryGetValue(e.Id, out var tier) ? tier : null)));
    }

    // Grant/revoke/promote/demote a Control Room Operator -- Command's two
    // permission tiers (see CommandOperator's own comment for why this
    // isn't a generic role/permission-code system). Only an Incident
    // Commander can change anyone's tier, including their own.
    [HttpPut("{id}/operator")]
    public async Task<IActionResult> SetOperator(Guid id, [FromBody] SetOperatorRequest request)
    {
        if (await RequireIncidentCommanderAsync() is ActionResult denied) return denied;
        if (!await db.Employees.AnyAsync(e => e.Id == id)) return NotFound();

        var existing = await db.CommandOperators.FirstOrDefaultAsync(o => o.EmployeeId == id);
        if (request.Tier is { } tier)
        {
            if (existing is null)
            {
                db.CommandOperators.Add(new CommandOperator { OrganisationId = OrganisationId, EmployeeId = id, Tier = tier });
            }
            else
            {
                existing.Tier = tier;
            }
        }
        else if (existing is not null)
        {
            db.CommandOperators.Remove(existing);
        }

        await db.SaveChangesAsync();
        return NoContent();
    }
}
