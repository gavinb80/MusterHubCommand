using Microsoft.AspNetCore.Mvc;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// The appliance tablet's own surface -- Device-token scoped, read-mostly.
// A device only ever sees its own station's incidents (DeviceOrgUnitId),
// enforced on every lookup rather than trusted from the client, since the
// device token itself carries no per-incident authorization. The only
// write is a crew note -- everything else about an incident (status,
// attendance, control-room updates) is Vision/operator-authored.
[Route("api/tablet/incidents")]
public class TabletIncidentsController(IncidentService incidentService) : DeviceControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<IncidentSummaryDto>>> List()
    {
        var incidents = await incidentService.ListAsync(OrganisationId, DeviceOrgUnitId, activeOnly: true);
        return Ok(incidents.Select(i => i.ToSummaryDto()));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<IncidentDto>> Get(Guid id)
    {
        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null || incident.OrgUnitId != DeviceOrgUnitId) return NotFound();
        return Ok(incident.ToDto());
    }

    [HttpPost("{id}/notes")]
    public async Task<ActionResult<IncidentDto>> AddNote(Guid id, AddCrewNoteRequest request)
    {
        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null || incident.OrgUnitId != DeviceOrgUnitId) return NotFound();

        var updated = await incidentService.AddUpdateAsync(
            OrganisationId, id, IncidentUpdateSource.Crew,
            null, request.AuthorEmployeeId, request.Text, IncidentUpdateType.Note);
        return Ok(updated!.ToDto());
    }
}
