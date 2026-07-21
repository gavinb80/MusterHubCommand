using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Routing;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// The appliance tablet's own surface -- Device-token scoped, read-mostly.
// A device only ever sees its own station's incidents (DeviceOrgUnitId),
// enforced on every lookup rather than trusted from the client, since the
// device token itself carries no per-incident authorization. The only
// write is a crew note -- everything else about an incident (status,
// attendance, control-room updates) is Vision/operator-authored.
[Route("api/tablet/incidents")]
public class TabletIncidentsController(IncidentService incidentService, ApplicationDbContext db, RoutingService routingService) : DeviceControllerBase
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

    // From this device's own last-reported GPS to the incident, using
    // whatever VehicleProfile this appliance is set up with in Setup.
    [HttpGet("{id}/route")]
    public async Task<ActionResult<RouteResponseDto>> GetRoute(Guid id)
    {
        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null || incident.OrgUnitId != DeviceOrgUnitId) return NotFound();
        if (incident.Latitude is null || incident.Longitude is null)
            return Ok(new RouteResponseDto(false, "This incident has no location to route to.", null, null, null, null));

        var device = await db.Devices.IgnoreQueryFilters()
            .Include(d => d.VehicleProfile)
            .FirstOrDefaultAsync(d => d.Id == DeviceId);
        if (device?.CurrentLatitude is null || device.CurrentLongitude is null)
            return Ok(new RouteResponseDto(false, "This tablet hasn't reported a location yet.", null, null, null, null));

        var (result, failure) = await routingService.ComputeRouteAsync(
            device.CurrentLatitude.Value, device.CurrentLongitude.Value,
            incident.Latitude.Value, incident.Longitude.Value, device.VehicleProfile);

        return Ok(result is not null ? result.ToDto() : failure!.Value.ToUnavailableDto());
    }

    // Entered from the incident detail screen when a crew leaves station.
    // Best-effort attendance update: if this device has no Callsign set in
    // Setup, there's nothing to match against the incident's attendance
    // list, so navigate mode still works as a display-only feature rather
    // than failing the whole request.
    [HttpPost("{id}/start-navigation")]
    public async Task<ActionResult<IncidentDto>> StartNavigation(Guid id)
    {
        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null || incident.OrgUnitId != DeviceOrgUnitId) return NotFound();

        var device = await db.Devices.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == DeviceId);
        if (!string.IsNullOrWhiteSpace(device?.Callsign))
        {
            var updated = await incidentService.SetSingleApplianceStatusAsync(OrganisationId, id, device.Callsign, ApplianceStatus.EnRoute);
            if (updated is not null) return Ok(updated.ToDto());
        }

        return Ok(incident.ToDto());
    }
}
