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
public class TabletIncidentsController(IncidentService incidentService, ApplicationDbContext db, RoutingService routingService, IFileStorage fileStorage) : DeviceControllerBase
{
    private const long MaxAttachmentBytes = 50 * 1024 * 1024;
    private static readonly HashSet<string> AllowedAttachmentContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/heic", "image/heif", "image/webp", "application/pdf",
    };
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
        try
        {
            var updated = await incidentService.AddUpdateAsync(
                OrganisationId, id, IncidentUpdateSource.Crew,
                DeviceCallsign, request.AuthorEmployeeId, request.Text, IncidentUpdateType.Note, request.ReplyToUpdateId);
            return Ok(updated!.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("{id}/updates/{updateId}/acknowledge")]
    public async Task<ActionResult<IncidentDto>> AcknowledgeUpdate(Guid id, Guid updateId)
    {
        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null || !AttendedByThisDevice(incident)) return NotFound();

        var updated = await incidentService.AcknowledgeUpdateAsync(OrganisationId, id, updateId, DeviceCallsign!);
        return Ok((updated ?? incident).ToDto());
    }

    // Same shape as the notes endpoint above -- device callsign as the
    // author, not a caller-supplied name.
    [HttpPost("{id}/actions")]
    public async Task<ActionResult<IncidentDto>> AddAction(Guid id, AddActionRequest request)
    {
        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null || !AttendedByThisDevice(incident)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest("Text is required.");

        try
        {
            var updated = await incidentService.RaiseActionAsync(
                OrganisationId, id, request.Kind, request.Text.Trim(), IncidentUpdateSource.Crew,
                DeviceCallsign, null,
                request.AssignedToEmployeeId, request.AssignedToName, request.SectorId);
            return Ok(updated!.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("{id}/actions/{actionId}/acknowledge")]
    public async Task<ActionResult<IncidentDto>> AcknowledgeAction(Guid id, Guid actionId)
    {
        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null || !AttendedByThisDevice(incident)) return NotFound();

        var updated = await incidentService.AcknowledgeActionAsync(OrganisationId, id, actionId, DeviceCallsign!);
        return Ok((updated ?? incident).ToDto());
    }

    [HttpPost("{id}/actions/{actionId}/resolve")]
    public async Task<ActionResult<IncidentDto>> ResolveAction(Guid id, Guid actionId, ResolveActionRequest request)
    {
        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null || !AttendedByThisDevice(incident)) return NotFound();

        try
        {
            var updated = await incidentService.ResolveActionAsync(OrganisationId, id, actionId, request.Status, DeviceCallsign!);
            return Ok((updated ?? incident).ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
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

    [HttpPost("{id}/attachments")]
    [RequestSizeLimit(MaxAttachmentBytes + 1024)]
    public async Task<ActionResult<IncidentAttachmentDto>> UploadAttachment(Guid id, IFormFile file)
    {
        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null || !AttendedByThisDevice(incident)) return NotFound();

        if (file.Length == 0) return BadRequest("File is empty.");
        if (file.Length > MaxAttachmentBytes) return BadRequest("File is too large (max 50MB).");
        if (!AllowedAttachmentContentTypes.Contains(file.ContentType)) return BadRequest($"Unsupported file type '{file.ContentType}'.");

        var attachment = new IncidentAttachment
        {
            OrganisationId = OrganisationId,
            IncidentId = id,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            StoragePath = "",
            UploadedByDeviceId = DeviceId,
        };
        attachment.StoragePath = $"{OrganisationId}/{id}/{attachment.Id}{Path.GetExtension(file.FileName)}";

        await using (var stream = file.OpenReadStream())
            await fileStorage.SaveAsync(attachment.StoragePath, stream, file.ContentType);

        db.IncidentAttachments.Add(attachment);
        await db.SaveChangesAsync();

        var deviceLabel = await db.Devices.IgnoreQueryFilters().Where(d => d.Id == DeviceId).Select(d => d.Label).FirstOrDefaultAsync();
        return Ok(new IncidentAttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.UploadedAtUtc, deviceLabel));
    }

    // Attendance-scoped, not a shared endpoint with the web console's own
    // /api/incident-attachments/{id} -- device-token and JWT auth don't
    // share a pipeline cleanly, and this route additionally has to check
    // the attachment's own incident is one this device is attending, not
    // just that any operator in the org can see it.
    [HttpGet("/api/tablet/incident-attachments/{attachmentId}")]
    public async Task<IActionResult> DownloadAttachment(Guid attachmentId)
    {
        // AttendedByThisDevice reads incident.Appliances -- without this
        // ThenInclude it silently evaluates against the entity's default
        // empty list rather than throwing, so every request looked
        // "not attending" regardless of actual attendance.
        var attachment = await db.IncidentAttachments.IgnoreQueryFilters()
            .Include(a => a.Incident).ThenInclude(i => i!.Appliances)
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.OrganisationId == OrganisationId);
        if (attachment?.Incident is null || !AttendedByThisDevice(attachment.Incident)) return NotFound();

        var stream = await fileStorage.OpenReadAsync(attachment.StoragePath);
        if (stream is null) return NotFound();
        return File(stream, attachment.ContentType, attachment.FileName);
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
