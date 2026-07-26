using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Routing;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// The appliance tablet's own surface -- Device-token scoped, read-mostly.
// A device sees only incidents its OWN callsign is attending, not every
// incident at its station -- a station can have more than one appliance
// out at once, each with its own tablet, and one crew has no business
// seeing full detail (address, description, notes) of a job a different
// appliance from the same station is on. No Callsign configured means no
// incidents at all (fail closed), not a fallback to station-wide
// visibility -- see AttendedByThisDevice. The only write is a crew note --
// everything else about an incident (status, attendance, control-room
// updates) is Vision/operator-authored.
[Route("api/tablet/incidents")]
public class TabletIncidentsController(IncidentService incidentService, ApplicationDbContext db, RoutingService routingService) : DeviceControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<IncidentSummaryDto>>> List()
    {
        var incidents = await incidentService.ListAsync(OrganisationId, DeviceOrgUnitId, activeOnly: true);
        return Ok(incidents.Where(AttendedByThisDevice).Select(i => i.ToSummaryDto()));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<IncidentDto>> Get(Guid id)
    {
        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null || !AttendedByThisDevice(incident)) return NotFound();
        return Ok(await incident.ToDtoWithLocationsAsync(db));
    }

    [HttpPost("{id}/notes")]
    public async Task<ActionResult<IncidentDto>> AddNote(Guid id, AddCrewNoteRequest request)
    {
        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null || !AttendedByThisDevice(incident)) return NotFound();

        // DeviceCallsign, not a hardcoded null -- AttendedByThisDevice above
        // already guarantees it's set and non-blank (that's the whole basis
        // of this device being allowed to see the incident at all), so a
        // crew note always reads as "KV57P1", not a bare, unattributed
        // "Crew note" indistinguishable from any other appliance's.
        var updated = await incidentService.AddUpdateAsync(
            OrganisationId, id, IncidentUpdateSource.Crew,
            DeviceCallsign, request.AuthorEmployeeId, request.Text, IncidentUpdateType.Note);
        return Ok(updated!.ToDto());
    }

    [HttpPost("{id}/updates/{updateId}/acknowledge")]
    public async Task<ActionResult<IncidentDto>> AcknowledgeUpdate(Guid id, Guid updateId)
    {
        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null || !AttendedByThisDevice(incident)) return NotFound();

        var updated = await incidentService.AcknowledgeUpdateAsync(OrganisationId, id, updateId, DeviceCallsign!);
        return Ok((updated ?? incident).ToDto());
    }

    // From this device's own last-reported GPS to the incident, using
    // whatever VehicleProfile this appliance is set up with in Setup.
    [HttpGet("{id}/route")]
    public async Task<ActionResult<RouteResponseDto>> GetRoute(Guid id)
    {
        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null || !AttendedByThisDevice(incident)) return NotFound();
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
    [HttpPost("{id}/start-navigation")]
    public async Task<ActionResult<IncidentDto>> StartNavigation(Guid id)
    {
        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null || !AttendedByThisDevice(incident)) return NotFound();

        var updated = await incidentService.SetSingleApplianceStatusAsync(OrganisationId, id, DeviceCallsign!, ApplianceStatus.EnRoute);
        return Ok((updated ?? incident).ToDto());
    }

    // Station match is necessary but not sufficient -- a device with no
    // Callsign configured (still possible: it's optional in Setup) can
    // never be "attending" anything, by design, rather than falling back
    // to the old station-wide visibility.
    private bool AttendedByThisDevice(Incident incident) =>
        incident.OrgUnitId == DeviceOrgUnitId &&
        !string.IsNullOrWhiteSpace(DeviceCallsign) &&
        incident.Appliances.Any(a => string.Equals(a.Callsign, DeviceCallsign, StringComparison.OrdinalIgnoreCase));
}
