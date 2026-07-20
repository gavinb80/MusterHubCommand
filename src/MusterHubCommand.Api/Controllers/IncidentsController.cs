using Microsoft.AspNetCore.Mvc;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// The control-room web console's own authoring surface -- same
// IncidentService underneath IntegrationIncidentsController, so a manually
// keyed incident and a Vision-pushed one behave identically. Operator-gated
// throughout: creating/editing incidents and pushing updates is exactly as
// sensitive as provisioning a device, see DevicesController.
[Route("api/incidents")]
public class IncidentsController(
    IncidentService incidentService,
    ICurrentOrganisationAccessor organisationAccessor,
    ICurrentEmployeeAccessor currentEmployeeAccessor,
    OperatorPermissionChecker operatorChecker)
    : CommandControllerBase(organisationAccessor, currentEmployeeAccessor, operatorChecker)
{
    [HttpGet]
    public async Task<ActionResult<List<IncidentSummaryDto>>> List([FromQuery] Guid? orgUnitId, [FromQuery] bool activeOnly = true)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var incidents = await incidentService.ListAsync(OrganisationId, orgUnitId, activeOnly);
        return Ok(incidents.Select(i => i.ToSummaryDto()));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<IncidentDto>> Get(Guid id)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        return incident is null ? NotFound() : Ok(incident.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<IncidentDto>> Create(CreateIncidentRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        try
        {
            var incident = await incidentService.CreateOrUpsertAsync(OrganisationId, request);
            return Ok(incident.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<IncidentDto>> Update(Guid id, UpdateIncidentRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        try
        {
            var updated = await incidentService.PatchAsync(OrganisationId, id, request);
            return updated is null ? NotFound() : Ok(updated.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.CancelAsync(OrganisationId, id);
        return updated is null ? NotFound() : NoContent();
    }

    [HttpPut("{id}/appliances")]
    public async Task<ActionResult<IncidentDto>> SetAppliances(Guid id, SetAppliancesRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.SetAppliancesAsync(OrganisationId, id, request);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPost("{id}/updates")]
    public async Task<ActionResult<IncidentDto>> AddUpdate(Guid id, AddIncidentUpdateRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.AddUpdateAsync(
            OrganisationId, id, IncidentUpdateSource.ControlRoom,
            request.AuthorName, null, request.Text, request.UpdateType);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }
}
