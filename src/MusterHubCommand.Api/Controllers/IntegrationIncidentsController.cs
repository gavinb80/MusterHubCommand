using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// The Vision-shaped push/update/delete API named in the plan. Machine-to-
// machine: AllowAnonymous with manual X-Api-Key validation, mirroring core's
// own integration endpoints and Rota's IntegrationShiftsController. Not
// specific to Vision -- any service's control-room system can push here the
// same way, which is the whole point of keeping Command generic.
[ApiController]
[Route("api/integrations/incidents")]
public class IntegrationIncidentsController(IncidentService incidentService, IntegrationApiKeyValidator keyValidator) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost]
    public async Task<ActionResult<IncidentDto>> Create(CreateIncidentRequest request)
    {
        var organisationId = await AuthenticateAsync();
        if (organisationId is null) return Unauthorized("Invalid API key.");

        try
        {
            var incident = await incidentService.CreateOrUpsertAsync(organisationId.Value, request);
            return Ok(incident.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [AllowAnonymous]
    [HttpPatch("{externalReference}")]
    public async Task<ActionResult<IncidentDto>> Update(string externalReference, UpdateIncidentRequest request)
    {
        var organisationId = await AuthenticateAsync();
        if (organisationId is null) return Unauthorized("Invalid API key.");

        var incident = await incidentService.FindByExternalReferenceAsync(organisationId.Value, externalReference);
        if (incident is null) return NotFound();

        try
        {
            var updated = await incidentService.PatchAsync(organisationId.Value, incident.Id, request);
            return Ok(updated!.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    // Soft: marks Status = Cancelled rather than removing the row, so a
    // tablet with the incident already open shows "cancelled" instead of it
    // vanishing mid-turnout.
    [AllowAnonymous]
    [HttpDelete("{externalReference}")]
    public async Task<IActionResult> Delete(string externalReference)
    {
        var organisationId = await AuthenticateAsync();
        if (organisationId is null) return Unauthorized("Invalid API key.");

        var incident = await incidentService.FindByExternalReferenceAsync(organisationId.Value, externalReference);
        if (incident is null) return NotFound();

        await incidentService.CancelAsync(organisationId.Value, incident.Id);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPut("{externalReference}/appliances")]
    public async Task<ActionResult<IncidentDto>> SetAppliances(string externalReference, SetAppliancesRequest request)
    {
        var organisationId = await AuthenticateAsync();
        if (organisationId is null) return Unauthorized("Invalid API key.");

        var incident = await incidentService.FindByExternalReferenceAsync(organisationId.Value, externalReference);
        if (incident is null) return NotFound();

        var updated = await incidentService.SetAppliancesAsync(organisationId.Value, incident.Id, request);
        return Ok(updated!.ToDto());
    }

    [AllowAnonymous]
    [HttpPost("{externalReference}/updates")]
    public async Task<ActionResult<IncidentDto>> AddUpdate(string externalReference, AddIncidentUpdateRequest request)
    {
        var organisationId = await AuthenticateAsync();
        if (organisationId is null) return Unauthorized("Invalid API key.");

        var incident = await incidentService.FindByExternalReferenceAsync(organisationId.Value, externalReference);
        if (incident is null) return NotFound();

        var updated = await incidentService.AddUpdateAsync(
            organisationId.Value, incident.Id, IncidentUpdateSource.ControlRoom,
            request.AuthorName, null, request.Text, request.UpdateType);
        return Ok(updated!.ToDto());
    }

    private Task<Guid?> AuthenticateAsync() =>
        keyValidator.ValidateAsync(Request.Headers.TryGetValue("X-Api-Key", out var key) ? key.ToString() : null);
}
