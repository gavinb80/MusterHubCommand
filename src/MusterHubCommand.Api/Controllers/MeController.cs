using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// The web console's own bootstrap check -- lets the frontend gate its UI
// (hide Setup, hide the operator-only actions) rather than relying solely
// on a 403 after the fact. The API still enforces every one of these
// server-side regardless of what this reports; this is purely so a plain
// signed-in user never sees a control they can't actually use. Same shape
// as Rota's MeController, simplified for Command's single-flag operator
// model (no manageable-org-unit set to compute).
[Route("api/me")]
public class MeController(
    ApplicationDbContext db,
    ICurrentOrganisationAccessor organisationAccessor,
    ICurrentEmployeeAccessor currentEmployeeAccessor,
    OperatorPermissionChecker operatorChecker)
    : CommandControllerBase(organisationAccessor, currentEmployeeAccessor, operatorChecker)
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var employeeId = await CurrentEmployeeIdAsync();
        var displayName = employeeId is null
            ? null
            : await db.Employees.Where(e => e.Id == employeeId).Select(e => e.DisplayName).FirstOrDefaultAsync();

        var isBootstrapping = await operatorChecker.IsBootstrappingAsync();
        var isOperator = isBootstrapping || (employeeId is not null && await operatorChecker.IsOperatorAsync(employeeId.Value));

        return Ok(new { organisationId = OrganisationId, employeeId, displayName, isOperator, isBootstrapping });
    }
}
