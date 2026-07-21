using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// An appliance class's physical routing constraints, managed from Setup --
// same operator-gated CRUD shape as Devices/IntegrationApiKeys. See
// VehicleProfile's own comment for why these values matter: RoutingService
// builds a real Itinero vehicle profile from them per Device.
[Route("api/vehicle-profiles")]
public class VehicleProfilesController(
    ApplicationDbContext db,
    ICurrentOrganisationAccessor organisationAccessor,
    ICurrentEmployeeAccessor currentEmployeeAccessor,
    OperatorPermissionChecker operatorChecker)
    : CommandControllerBase(organisationAccessor, currentEmployeeAccessor, operatorChecker)
{
    [HttpGet]
    public async Task<ActionResult<List<VehicleProfileDto>>> List()
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var profiles = await db.VehicleProfiles.OrderBy(p => p.Name).ToListAsync();
        return Ok(profiles.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<VehicleProfileDto>> Create(SaveVehicleProfileRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");

        var profile = new VehicleProfile
        {
            OrganisationId = OrganisationId,
            Name = request.Name.Trim(),
            MaxWeightTonnes = request.MaxWeightTonnes,
            MaxHeightMetres = request.MaxHeightMetres,
            MaxWidthMetres = request.MaxWidthMetres,
        };
        db.VehicleProfiles.Add(profile);
        await db.SaveChangesAsync();
        return Ok(ToDto(profile));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<VehicleProfileDto>> Update(Guid id, SaveVehicleProfileRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");

        var profile = await db.VehicleProfiles.FirstOrDefaultAsync(p => p.Id == id);
        if (profile is null) return NotFound();

        profile.Name = request.Name.Trim();
        profile.MaxWeightTonnes = request.MaxWeightTonnes;
        profile.MaxHeightMetres = request.MaxHeightMetres;
        profile.MaxWidthMetres = request.MaxWidthMetres;
        await db.SaveChangesAsync();
        return Ok(ToDto(profile));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var profile = await db.VehicleProfiles.FirstOrDefaultAsync(p => p.Id == id);
        if (profile is null) return NotFound();

        // Devices referencing this profile fall back to unrestricted
        // routing (VehicleProfileId's FK is ON DELETE SET NULL) rather than
        // blocking the delete -- an Operator retiring an appliance class
        // shouldn't have to first hunt down every device that used it.
        db.VehicleProfiles.Remove(profile);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static VehicleProfileDto ToDto(VehicleProfile p) =>
        new(p.Id, p.Name, p.MaxWeightTonnes, p.MaxHeightMetres, p.MaxWidthMetres);
}
