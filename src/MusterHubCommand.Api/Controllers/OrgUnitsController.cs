using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// Read-only -- Command has no Setup UI for editing the org hierarchy by
// hand (unlike Rota/Skills), it's a passive mirror of core via directory
// sync. This just lists what's there, for the incident/device station
// pickers.
[Route("api/org-units")]
public class OrgUnitsController(
    ApplicationDbContext db,
    ICurrentOrganisationAccessor organisationAccessor,
    ICurrentEmployeeAccessor currentEmployeeAccessor,
    OperatorPermissionChecker operatorChecker)
    : CommandControllerBase(organisationAccessor, currentEmployeeAccessor, operatorChecker)
{
    [HttpGet]
    public async Task<ActionResult<List<OrgUnitDto>>> List()
    {
        var units = await db.OrgUnits
            .Include(u => u.OrgUnitType)
            .Where(u => u.IsActive)
            .OrderBy(u => u.Path)
            .ToListAsync();

        return Ok(units.Select(u => new OrgUnitDto(u.Id, u.Name, u.Code, u.OrgUnitType!.Name, u.ParentId)));
    }
}
