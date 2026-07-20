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
        var operatorIds = await db.CommandOperators.Select(o => o.EmployeeId).ToHashSetAsync();
        var employees = await db.Employees.OrderBy(e => e.DisplayName).ToListAsync();
        return Ok(employees.Select(e => new EmployeeDto(e.Id, e.DisplayName, e.EmployeeNumber, operatorIds.Contains(e.Id))));
    }

    // Grant/revoke Control Room Operator -- Command's one and only
    // permission (see CommandOperator's own comment for why this isn't a
    // generic role/permission-code system).
    [HttpPut("{id}/operator")]
    public async Task<IActionResult> SetOperator(Guid id, [FromBody] bool isOperator)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        if (!await db.Employees.AnyAsync(e => e.Id == id)) return NotFound();

        var existing = await db.CommandOperators.FirstOrDefaultAsync(o => o.EmployeeId == id);
        if (isOperator && existing is null)
        {
            db.CommandOperators.Add(new CommandOperator { OrganisationId = OrganisationId, EmployeeId = id });
        }
        else if (!isOperator && existing is not null)
        {
            db.CommandOperators.Remove(existing);
        }

        await db.SaveChangesAsync();
        return NoContent();
    }
}
