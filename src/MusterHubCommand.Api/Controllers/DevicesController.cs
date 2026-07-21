using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// Device provisioning/revocation -- the web console's Setup screen for
// pairing a tablet. All Operator-gated: a device token effectively is a
// station's worth of read/write access to live incidents, so minting one
// is exactly as sensitive as minting an integration API key.
[Route("api/devices")]
public class DevicesController(
    ApplicationDbContext db,
    ICurrentOrganisationAccessor organisationAccessor,
    ICurrentEmployeeAccessor currentEmployeeAccessor,
    OperatorPermissionChecker operatorChecker)
    : CommandControllerBase(organisationAccessor, currentEmployeeAccessor, operatorChecker)
{
    [HttpGet]
    public async Task<ActionResult<List<DeviceDto>>> List()
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var devices = await db.Devices.Include(d => d.OrgUnit).Include(d => d.VehicleProfile).OrderBy(d => d.Label).ToListAsync();
        return Ok(devices.Select(ToDto));
    }

    // Assigning null clears it -- back to unrestricted routing, same as a
    // device that was never given one.
    [HttpPut("{id}/vehicle-profile")]
    public async Task<ActionResult<DeviceDto>> SetVehicleProfile(Guid id, [FromBody] Guid? vehicleProfileId)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var device = await db.Devices.Include(d => d.OrgUnit).FirstOrDefaultAsync(d => d.Id == id);
        if (device is null) return NotFound();

        if (vehicleProfileId is not null && !await db.VehicleProfiles.AnyAsync(p => p.Id == vehicleProfileId))
            return BadRequest("No such vehicle profile.");

        device.VehicleProfileId = vehicleProfileId;
        await db.SaveChangesAsync();

        device.VehicleProfile = vehicleProfileId is null ? null : await db.VehicleProfiles.FirstAsync(p => p.Id == vehicleProfileId);
        return Ok(ToDto(device));
    }

    private static DeviceDto ToDto(Device d) => new(
        d.Id, d.Label, d.OrgUnitId, d.OrgUnit!.Name,
        d.VehicleProfileId, d.VehicleProfile?.Name,
        d.CurrentLatitude, d.CurrentLongitude, d.LocationUpdatedAtUtc,
        d.CreatedAtUtc, d.LastSeenAtUtc, d.IsActive);

    [HttpPost]
    public async Task<ActionResult<CreateDeviceResponse>> Create(CreateDeviceRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        if (string.IsNullOrWhiteSpace(request.Label)) return BadRequest("Label is required.");
        if (!await db.OrgUnits.AnyAsync(u => u.Id == request.OrgUnitId))
            return BadRequest("OrgUnitId does not reference an existing station.");

        var token = SecretHasher.GenerateToken();
        var device = new Device
        {
            OrganisationId = OrganisationId,
            OrgUnitId = request.OrgUnitId,
            Label = request.Label.Trim(),
            TokenHash = SecretHasher.Hash(token),
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync();

        return Ok(new CreateDeviceResponse(device.Id, device.Label, token));
    }

    // Soft: flips IsActive rather than deleting, so the device row (and its
    // LastSeenAtUtc history) survives a revocation -- consistent with how
    // core's own ApiKeyEntity revocation works.
    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id);
        if (device is null) return NotFound();

        device.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
