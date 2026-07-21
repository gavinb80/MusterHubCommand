using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// The appliance's own GPS, reported periodically by the tablet mounted on
// it -- see Device.CurrentLatitude's own comment for why this isn't fed
// from a separate AVL system. Device-scoped by construction: a tablet can
// only ever update its own paired device's position, never another one's.
[Route("api/tablet/location")]
public class TabletLocationController(ApplicationDbContext db, GeofenceService geofenceService) : DeviceControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Update(UpdateDeviceLocationRequest request)
    {
        var device = await db.Devices.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == DeviceId);
        if (device is null) return NotFound();

        device.CurrentLatitude = request.Latitude;
        device.CurrentLongitude = request.Longitude;
        device.LocationUpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        // Every report checks, not just while navigate mode is open -- an
        // appliance that never tapped "Start navigation" still auto-arrives
        // via the normal background cadence.
        await geofenceService.CheckAndMarkOnSceneAsync(OrganisationId, DeviceOrgUnitId, device.Callsign, request.Latitude, request.Longitude);

        return NoContent();
    }
}
