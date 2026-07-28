using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;

namespace MusterHubCommand.Api.Controllers;

// Read-only, Device-token counterpart to OrganisationSettingsController --
// a tablet can't call the operator-gated JWT one, but it needs the
// geofence radius to draw the arrival zone on its own map. Same default-
// if-no-row semantics as the operator endpoint.
[Route("api/tablet/organisation-settings")]
public class TabletOrganisationSettingsController(ApplicationDbContext db) : DeviceControllerBase
{
    private const double DefaultGeofenceRadiusMeters = 50;

    [HttpGet]
    public async Task<ActionResult<OrganisationSettingsDto>> Get()
    {
        var settings = await db.OrganisationSettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.OrganisationId == OrganisationId);
        return Ok(new OrganisationSettingsDto(
            settings?.GeofenceRadiusMeters ?? DefaultGeofenceRadiusMeters, settings?.BaEntryControlEnabled ?? false));
    }
}
