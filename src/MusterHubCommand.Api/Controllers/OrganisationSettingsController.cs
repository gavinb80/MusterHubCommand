using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// Org-wide settings, managed from Setup > General. One row per org,
// created lazily on first save -- GET before that just returns the
// default rather than materializing a row nobody's touched yet.
[Route("api/organisation-settings")]
public class OrganisationSettingsController(
    ApplicationDbContext db,
    ICurrentOrganisationAccessor organisationAccessor,
    ICurrentEmployeeAccessor currentEmployeeAccessor,
    OperatorPermissionChecker operatorChecker)
    : CommandControllerBase(organisationAccessor, currentEmployeeAccessor, operatorChecker)
{
    private const double DefaultGeofenceRadiusMeters = 50;

    [HttpGet]
    public async Task<ActionResult<OrganisationSettingsDto>> Get()
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var settings = await db.OrganisationSettings.FirstOrDefaultAsync();
        return Ok(new OrganisationSettingsDto(
            settings?.GeofenceRadiusMeters ?? DefaultGeofenceRadiusMeters, settings?.BaEntryControlEnabled ?? false));
    }

    [HttpPut]
    public async Task<ActionResult<OrganisationSettingsDto>> Update(UpdateOrganisationSettingsRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        if (request.GeofenceRadiusMeters <= 0) return BadRequest("Geofence radius must be a positive number of metres.");

        var settings = await db.OrganisationSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new OrganisationSettings
            {
                OrganisationId = OrganisationId,
                GeofenceRadiusMeters = request.GeofenceRadiusMeters,
                BaEntryControlEnabled = request.BaEntryControlEnabled,
            };
            db.OrganisationSettings.Add(settings);
        }
        else
        {
            settings.GeofenceRadiusMeters = request.GeofenceRadiusMeters;
            settings.BaEntryControlEnabled = request.BaEntryControlEnabled;
        }
        await db.SaveChangesAsync();
        return Ok(new OrganisationSettingsDto(settings.GeofenceRadiusMeters, settings.BaEntryControlEnabled));
    }
}
