using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// The org's own close-out classification scheme, managed from Setup --
// same operator-gated CRUD shape as VehicleProfiles.
[Route("api/incident-close-types")]
public class IncidentCloseTypesController(
    ApplicationDbContext db,
    ICurrentOrganisationAccessor organisationAccessor,
    ICurrentEmployeeAccessor currentEmployeeAccessor,
    OperatorPermissionChecker operatorChecker)
    : CommandControllerBase(organisationAccessor, currentEmployeeAccessor, operatorChecker)
{
    [HttpGet]
    public async Task<ActionResult<List<IncidentCloseTypeDto>>> List()
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var types = await db.IncidentCloseTypes.OrderBy(t => t.Code).ToListAsync();
        return Ok(types.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<IncidentCloseTypeDto>> Create(SaveIncidentCloseTypeRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Code and name are required.");

        var closeType = new IncidentCloseType
        {
            OrganisationId = OrganisationId,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
        };
        db.IncidentCloseTypes.Add(closeType);
        await db.SaveChangesAsync();
        return Ok(ToDto(closeType));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<IncidentCloseTypeDto>> Update(Guid id, SaveIncidentCloseTypeRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Code and name are required.");

        var closeType = await db.IncidentCloseTypes.FirstOrDefaultAsync(t => t.Id == id);
        if (closeType is null) return NotFound();

        closeType.Code = request.Code.Trim();
        closeType.Name = request.Name.Trim();
        await db.SaveChangesAsync();
        return Ok(ToDto(closeType));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var closeType = await db.IncidentCloseTypes.FirstOrDefaultAsync(t => t.Id == id);
        if (closeType is null) return NotFound();

        // Incidents referencing this type keep their CloseTypeId set to a
        // now-dangling value rather than being blocked or nulled out --
        // FK is ON DELETE RESTRICT (see migration), so this only succeeds
        // once no incident still references it. A retired code shouldn't
        // silently rewrite history on incidents already closed against it.
        db.IncidentCloseTypes.Remove(closeType);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static IncidentCloseTypeDto ToDto(IncidentCloseType t) => new(t.Id, t.Code, t.Name);
}
