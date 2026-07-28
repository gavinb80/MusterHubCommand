using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Routing;
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
    ApplicationDbContext db,
    RoutingService routingService,
    IFileStorage fileStorage,
    ICurrentOrganisationAccessor organisationAccessor,
    ICurrentEmployeeAccessor currentEmployeeAccessor,
    OperatorPermissionChecker operatorChecker)
    : CommandControllerBase(organisationAccessor, currentEmployeeAccessor, operatorChecker)
{
    private const long MaxAttachmentBytes = 50 * 1024 * 1024;
    private static readonly HashSet<string> AllowedAttachmentContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/heic", "image/heif", "image/webp", "application/pdf",
    };
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
        return incident is null ? NotFound() : Ok(await incident.ToDtoWithLocationsAsync(db));
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
        // Closing is elevated (same tier as Cancel below); any other field
        // on this same generic PATCH -- description, address, etc. -- is
        // still just baseline operator access.
        var denied = request.Status == IncidentStatus.Closed
            ? await RequireIncidentCommanderAsync()
            : await RequireOperatorAsync();
        if (denied is ActionResult result) return result;

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
        if (await RequireIncidentCommanderAsync() is ActionResult denied) return denied;

        var updated = await incidentService.CancelAsync(OrganisationId, id);
        return updated is null ? NotFound() : NoContent();
    }

    // Its own endpoint rather than folded into the generic PATCH above --
    // same IncidentCommander gate PATCH already applies for Status=Closed,
    // but this is the one path that also carries the structured close-out.
    [HttpPost("{id}/close")]
    public async Task<ActionResult<IncidentDto>> Close(Guid id, CloseIncidentRequest request)
    {
        if (await RequireIncidentCommanderAsync() is ActionResult denied) return denied;

        try
        {
            var updated = await incidentService.CloseAsync(OrganisationId, id, request);
            return updated is null ? NotFound() : Ok(updated.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPut("{id}/appliances")]
    public async Task<ActionResult<IncidentDto>> SetAppliances(Guid id, SetAppliancesRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.SetAppliancesAsync(OrganisationId, id, request);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPost("{id}/sectors")]
    public async Task<ActionResult<IncidentDto>> AddSector(Guid id, AddSectorRequest request)
    {
        if (await RequireIncidentCommanderAsync() is ActionResult denied) return denied;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Sector name is required.");

        try
        {
            var updated = await incidentService.AddSectorAsync(
                OrganisationId, id, request.Name.Trim(),
                request.ParentId, request.PersonInChargeEmployeeId, request.PersonInChargeName);
            return updated is null ? NotFound() : Ok(updated.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPatch("{id}/sectors/{sectorId}")]
    public async Task<ActionResult<IncidentDto>> UpdateSector(Guid id, Guid sectorId, UpdateSectorRequest request)
    {
        if (await RequireIncidentCommanderAsync() is ActionResult denied) return denied;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Sector name is required.");

        try
        {
            var updated = await incidentService.UpdateSectorAsync(
                OrganisationId, id, sectorId, request.Name.Trim(),
                request.ParentId, request.PersonInChargeEmployeeId, request.PersonInChargeName);
            return updated is null ? NotFound() : Ok(updated.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("{id}/sectors/{sectorId}")]
    public async Task<ActionResult<IncidentDto>> DeleteSector(Guid id, Guid sectorId)
    {
        if (await RequireIncidentCommanderAsync() is ActionResult denied) return denied;

        var updated = await incidentService.DeleteSectorAsync(OrganisationId, id, sectorId);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPatch("{id}/appliances/{applianceId}/sector")]
    public async Task<ActionResult<IncidentDto>> AssignApplianceSector(Guid id, Guid applianceId, AssignSectorRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        try
        {
            var updated = await incidentService.AssignApplianceSectorAsync(OrganisationId, id, applianceId, request.SectorId);
            return updated is null ? NotFound() : Ok(updated.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPatch("{id}/appliances/{applianceId}/status")]
    public async Task<ActionResult<IncidentDto>> SetApplianceStatus(Guid id, Guid applianceId, SetApplianceStatusRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.SetApplianceStatusAsync(OrganisationId, id, applianceId, request.Status);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPatch("{id}/appliances/{applianceId}/resource-kind")]
    public async Task<ActionResult<IncidentDto>> SetApplianceResourceKind(Guid id, Guid applianceId, SetResourceKindRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.SetApplianceResourceKindAsync(OrganisationId, id, applianceId, request.ResourceKind);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPatch("{id}/appliances/{applianceId}/officer")]
    public async Task<ActionResult<IncidentDto>> SetApplianceOfficer(Guid id, Guid applianceId, SetApplianceOfficerRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.SetApplianceOfficerAsync(
            OrganisationId, id, applianceId, request.OfficerInChargeEmployeeId, request.OfficerInChargeName);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPost("{id}/updates")]
    public async Task<ActionResult<IncidentDto>> AddUpdate(Guid id, AddIncidentUpdateRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        try
        {
            var updated = await incidentService.AddUpdateAsync(
                OrganisationId, id, IncidentUpdateSource.ControlRoom,
                request.AuthorName, null, request.Text, request.UpdateType, request.ReplyToUpdateId);
            return updated is null ? NotFound() : Ok(updated.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("{id}/updates/{updateId}/acknowledge")]
    public async Task<ActionResult<IncidentDto>> AcknowledgeUpdate(Guid id, Guid updateId, AcknowledgeUpdateRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.AcknowledgeUpdateAsync(OrganisationId, id, updateId, request.AcknowledgedByName ?? "Control Room");
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPost("{id}/actions")]
    public async Task<ActionResult<IncidentDto>> AddAction(Guid id, AddActionRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest("Text is required.");

        try
        {
            var updated = await incidentService.RaiseActionAsync(
                OrganisationId, id, request.Kind, request.Text.Trim(), IncidentUpdateSource.ControlRoom,
                request.RaisedByName, null,
                request.AssignedToEmployeeId, request.AssignedToName, request.SectorId);
            return updated is null ? NotFound() : Ok(updated.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("{id}/actions/{actionId}/acknowledge")]
    public async Task<ActionResult<IncidentDto>> AcknowledgeAction(Guid id, Guid actionId, AcknowledgeActionRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.AcknowledgeActionAsync(OrganisationId, id, actionId, request.AcknowledgedByName ?? "Control Room");
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPost("{id}/actions/{actionId}/resolve")]
    public async Task<ActionResult<IncidentDto>> ResolveAction(Guid id, Guid actionId, ResolveActionRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        try
        {
            var updated = await incidentService.ResolveActionAsync(OrganisationId, id, actionId, request.Status, request.ResolvedByName ?? "Control Room");
            return updated is null ? NotFound() : Ok(updated.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("{id}/objectives")]
    public async Task<ActionResult<IncidentDto>> AddObjective(Guid id, AddObjectiveRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest("Text is required.");

        var (name, employeeId) = await ResolveCallerAsync();
        var updated = await incidentService.RaiseObjectiveAsync(
            OrganisationId, id, request.Text.Trim(), IncidentUpdateSource.ControlRoom, name, employeeId);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPost("{id}/objectives/{objectiveId}/achieve")]
    public async Task<ActionResult<IncidentDto>> AchieveObjective(Guid id, Guid objectiveId)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var (name, _) = await ResolveCallerAsync();
        var updated = await incidentService.AchieveObjectiveAsync(OrganisationId, id, objectiveId, name);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPost("{id}/objectives/{objectiveId}/reopen")]
    public async Task<ActionResult<IncidentDto>> ReopenObjective(Guid id, Guid objectiveId)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.ReopenObjectiveAsync(OrganisationId, id, objectiveId);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPost("{id}/risks")]
    public async Task<ActionResult<IncidentDto>> AddRisk(Guid id, AddRiskRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        if (string.IsNullOrWhiteSpace(request.Description)) return BadRequest("Description is required.");

        var (name, employeeId) = await ResolveCallerAsync();
        var updated = await incidentService.RaiseRiskAsync(
            OrganisationId, id, request.Description.Trim(), request.RiskLevel, request.ControlMeasure?.Trim(),
            IncidentUpdateSource.ControlRoom, name, employeeId);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPost("{id}/risks/{riskId}/control")]
    public async Task<ActionResult<IncidentDto>> ControlRisk(Guid id, Guid riskId)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var (name, _) = await ResolveCallerAsync();
        var updated = await incidentService.ControlRiskAsync(OrganisationId, id, riskId, name);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPost("{id}/risks/{riskId}/reopen")]
    public async Task<ActionResult<IncidentDto>> ReopenRisk(Guid id, Guid riskId)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.ReopenRiskAsync(OrganisationId, id, riskId);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    // No write endpoints for BA Entry Control here -- the web console only
    // ever reads it (already covered by GET /{id}'s own IncidentDto.BaEntryControlPoints).
    // The ECO is physically at the entry control point with a tablet, not
    // a remote control-room operator, so every create/exit action for a
    // BA board lives on TabletIncidentsController instead.

    // RaisedByName/AchievedByName on an objective are always the caller's
    // own identity, never a caller-supplied field -- see
    // AddObjectiveRequest's own comment. Falls back to "Control Room" the
    // same way AcknowledgeAction/ResolveAction already default their own
    // optional -ByName fields.
    private async Task<(string Name, Guid? EmployeeId)> ResolveCallerAsync()
    {
        var employeeId = await CurrentEmployeeIdAsync();
        if (employeeId is null) return ("Control Room", null);
        var name = await db.Employees.Where(e => e.Id == employeeId).Select(e => e.DisplayName).FirstOrDefaultAsync();
        return (name ?? "Control Room", employeeId);
    }

    // Same route computation as the tablet's own GET .../route, but for a
    // control-room operator picking any device (not just "this tablet's
    // own") to check against -- e.g. comparing which of two attending
    // appliances is genuinely closer once road access is accounted for.
    [HttpGet("{id}/route")]
    public async Task<ActionResult<RouteResponseDto>> GetRoute(Guid id, [FromQuery] Guid deviceId)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null) return NotFound();
        if (incident.Latitude is null || incident.Longitude is null)
            return Ok(new RouteResponseDto(false, "This incident has no location to route to.", null, null, null, null));

        var device = await db.Devices.Include(d => d.VehicleProfile).FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device is null) return NotFound();
        if (device.CurrentLatitude is null || device.CurrentLongitude is null)
            return Ok(new RouteResponseDto(false, "That device hasn't reported a location yet.", null, null, null, null));

        var (result, failure) = await routingService.ComputeRouteAsync(
            device.CurrentLatitude.Value, device.CurrentLongitude.Value,
            incident.Latitude.Value, incident.Longitude.Value, device.VehicleProfile);

        return Ok(result is not null ? result.ToDto() : failure!.Value.ToUnavailableDto());
    }

    [HttpPost("{id}/attachments")]
    [RequestSizeLimit(MaxAttachmentBytes + 1024)]
    public async Task<ActionResult<IncidentAttachmentDto>> UploadAttachment(Guid id, IFormFile file)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null) return NotFound();

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
            UploadedByEmployeeId = await CurrentEmployeeIdAsync(),
        };
        attachment.StoragePath = $"{OrganisationId}/{id}/{attachment.Id}{Path.GetExtension(file.FileName)}";

        await using (var stream = file.OpenReadStream())
            await fileStorage.SaveAsync(attachment.StoragePath, stream, file.ContentType);

        db.IncidentAttachments.Add(attachment);
        await db.SaveChangesAsync();

        var employeeName = attachment.UploadedByEmployeeId is { } employeeId
            ? await db.Employees.IgnoreQueryFilters().Where(e => e.Id == employeeId).Select(e => e.DisplayName).FirstOrDefaultAsync()
            : null;
        return Ok(new IncidentAttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.UploadedAtUtc, employeeName));
    }

    // Not nested under api/incidents/{id} -- an attachment is looked up by
    // its own id, same reasoning as api/incident-attachments in Skills.
    [HttpGet("/api/incident-attachments/{attachmentId}")]
    public async Task<IActionResult> DownloadAttachment(Guid attachmentId)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var attachment = await db.IncidentAttachments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.OrganisationId == OrganisationId);
        if (attachment is null) return NotFound();

        var stream = await fileStorage.OpenReadAsync(attachment.StoragePath);
        if (stream is null) return NotFound();
        return File(stream, attachment.ContentType, attachment.FileName);
    }

    [HttpDelete("/api/incident-attachments/{attachmentId}")]
    public async Task<IActionResult> DeleteAttachment(Guid attachmentId)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var attachment = await db.IncidentAttachments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.OrganisationId == OrganisationId);
        if (attachment is null) return NotFound();

        await fileStorage.DeleteAsync(attachment.StoragePath);
        db.IncidentAttachments.Remove(attachment);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
