using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// The web console's own bootstrap check -- lets the frontend gate its UI
// (hide Setup, hide the operator-only actions) rather than relying solely
// on a 403 after the fact. The API still enforces every one of these
// server-side regardless of what this reports; this is purely so a plain
// signed-in user never sees a control they can't actually use. Same shape
// as Rota's MeController, simplified for Command's two-tier operator model
// (no manageable-org-unit set to compute).
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

        // Runs before the operator/tier lookups below so a freshly-designated
        // module admin's very first request already reflects the grant it
        // just made, rather than reporting isBootstrapping=true for one
        // extra round trip. See EnsureModuleAdminGrantedAsync for why this
        // is the mechanism that closes the bootstrap window for a real org.
        if (employeeId is not null)
        {
            var isDesignatedModuleAdmin = User.HasClaim(c => c.Type == "module_admin" && c.Value == "command");
            await operatorChecker.EnsureModuleAdminGrantedAsync(OrganisationId, employeeId.Value, isDesignatedModuleAdmin);
        }

        var displayName = employeeId is null
            ? null
            : await db.Employees.Where(e => e.Id == employeeId).Select(e => e.DisplayName).FirstOrDefaultAsync();

        var isBootstrapping = await operatorChecker.IsBootstrappingAsync();
        var isOperator = isBootstrapping || (employeeId is not null && await operatorChecker.IsOperatorAsync(employeeId.Value));
        var operatorTier = employeeId is null ? null : await operatorChecker.GetTierAsync(employeeId.Value);
        var isIncidentCommander = operatorTier == CommandOperatorTier.IncidentCommander;

        return Ok(new { organisationId = OrganisationId, employeeId, displayName, isOperator, isBootstrapping, operatorTier, isIncidentCommander });
    }
}
