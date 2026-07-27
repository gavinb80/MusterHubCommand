using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Contracts;

// OfficerInChargeName is always the display name -- resolved server-side
// from Employee.DisplayName when OfficerInChargeEmployeeId is set, same
// rule IncidentSectorDto.PersonInChargeName already follows.
public record IncidentApplianceDto(
    Guid Id, string Callsign, ApplianceStatus Status, ResourceKind ResourceKind, Guid? SectorId,
    Guid? OfficerInChargeEmployeeId, string? OfficerInChargeName,
    double? Latitude, double? Longitude, DateTimeOffset? LocationUpdatedAtUtc, DateTimeOffset UpdatedAtUtc);

// PersonInChargeName is always the display name -- resolved server-side
// from Employee.DisplayName when PersonInChargeEmployeeId is set, or the
// free-text name otherwise, so clients never need a second lookup just to
// show who's in charge of a node.
public record IncidentSectorDto(
    Guid Id, string Name, int SortOrder, Guid? ParentId,
    Guid? PersonInChargeEmployeeId, string? PersonInChargeName);

public record IncidentUpdateDto(
    Guid Id, IncidentUpdateSource Source, string? AuthorName, Guid? AuthorEmployeeId,
    string Text, IncidentUpdateType UpdateType,
    DateTimeOffset? AcknowledgedAtUtc, string? AcknowledgedByName,
    Guid? ReplyToUpdateId, DateTimeOffset CreatedAtUtc);

// RaisedByName/AchievedByName are always resolved server-side from the
// caller's own session (the signed-in operator, or a tablet's
// DeviceCallsign) -- never caller-supplied, unlike
// IncidentActionDto.raisedByName. No Kind/AssignedToName/SectorId here:
// an objective belongs to the incident as a whole, not a person, appliance
// or category.
public record IncidentObjectiveDto(
    Guid Id, string Text, IncidentObjectiveStatus Status,
    IncidentUpdateSource Source, string RaisedByName, Guid? RaisedByEmployeeId,
    DateTimeOffset? AchievedAtUtc, string? AchievedByName, DateTimeOffset CreatedAtUtc);

// AssignedToName/RaisedByName are always the display name -- resolved
// server-side the same way IncidentSectorDto.personInChargeName is.
public record IncidentActionDto(
    Guid Id, IncidentActionKind Kind, string Text, IncidentActionStatus Status,
    IncidentUpdateSource Source, string? RaisedByName, Guid? RaisedByEmployeeId,
    Guid? AssignedToEmployeeId, string? AssignedToName, Guid? SectorId,
    DateTimeOffset? AcknowledgedAtUtc, string? AcknowledgedByName,
    DateTimeOffset? ResolvedAtUtc, string? ResolvedByName, DateTimeOffset CreatedAtUtc);

// CloseTypeCode/CloseTypeName are always the resolved values from
// IncidentCloseType when CloseTypeId is set -- same "ID + server-resolved
// display" pattern as OfficerInChargeName/PersonInChargeName elsewhere in
// this file, so clients never need a second lookup just to show it.
public record IncidentDto(
    Guid Id, string ExternalReference, string IncidentType, string? Description,
    string? Address, double? Latitude, double? Longitude,
    Guid OrgUnitId, string OrgUnitName, IncidentStatus Status,
    DateTimeOffset StartedAtUtc, DateTimeOffset? ClosedAtUtc, DateTimeOffset UpdatedAtUtc,
    Guid? CloseTypeId, string? CloseTypeCode, string? CloseTypeName,
    string? CloseActionsTaken, string? CloseOutcome,
    List<IncidentApplianceDto> Appliances, List<IncidentUpdateDto> Updates,
    List<IncidentSectorDto> Sectors, List<IncidentObjectiveDto> Objectives, List<IncidentActionDto> Actions,
    List<IncidentAttachmentDto> Attachments);

// UploadedByName is always resolved server-side -- the uploading
// employee's DisplayName for a web upload, or the uploading device's
// Label for a tablet upload (exactly one of UploadedByEmployeeId/
// UploadedByDeviceId is ever set, see IncidentAttachment's own comment).
public record IncidentAttachmentDto(
    Guid Id, string FileName, string ContentType, long SizeBytes,
    DateTimeOffset UploadedAtUtc, string? UploadedByName);

// The list view -- no appliances/updates payload, kept light for a
// station's "what's active" screen (tablet home and control room's list).
public record IncidentSummaryDto(
    Guid Id, string ExternalReference, string IncidentType, string? Address,
    Guid OrgUnitId, string OrgUnitName, IncidentStatus Status,
    DateTimeOffset StartedAtUtc, DateTimeOffset UpdatedAtUtc);

// StationCode matches OrgUnit.Code -- Vision (or the control-room operator
// form) names a station the way its own system knows it, not by Command's
// internal Guid.
public record CreateIncidentRequest(
    string ExternalReference, string IncidentType, string? Description,
    string? Address, double? Latitude, double? Longitude,
    string StationCode, DateTimeOffset? StartedAtUtc);

// PATCH: every field optional, only the ones present are applied.
public record UpdateIncidentRequest(
    string? IncidentType, string? Description, string? Address,
    double? Latitude, double? Longitude, string? StationCode, IncidentStatus? Status);

// Closing with a structured summary is its own endpoint (POST /{id}/close),
// mirroring Cancel's own DELETE rather than folding into the generic PATCH
// -- same IncidentCommander gate as PATCH's existing Status=Closed branch.
// All fields optional: a summary is a good practice, not a hard gate on
// being able to close an incident at all. CloseTypeId is a reference into
// the org's own managed classification scheme, not free text.
public record CloseIncidentRequest(Guid? CloseTypeId, string? ActionsTaken, string? Outcome);

public record IncidentCloseTypeDto(Guid Id, string Code, string Name);
public record SaveIncidentCloseTypeRequest(string Code, string Name);

public record SetApplianceEntry(string Callsign, ApplianceStatus Status);
public record SetAppliancesRequest(List<SetApplianceEntry> Appliances);

public record AddSectorRequest(
    string Name, Guid? ParentId = null,
    Guid? PersonInChargeEmployeeId = null, string? PersonInChargeName = null);

// Full-replace, not a sparse patch: ParentId/PersonInCharge are themselves
// nullable (null parent = a root, null person = nobody assigned yet), so
// there's no spare bit left to mean "leave this one alone" the way
// UpdateIncidentRequest's optional fields do. The web edit form always has
// the node's current state loaded already, so submitting the complete
// desired state costs it nothing.
public record UpdateSectorRequest(
    string Name, Guid? ParentId,
    Guid? PersonInChargeEmployeeId, string? PersonInChargeName);

public record AssignSectorRequest(Guid? SectorId);
public record SetResourceKindRequest(ResourceKind ResourceKind);

// Same full-replace reasoning as UpdateSectorRequest -- EmployeeId is
// itself nullable (null = nobody assigned), so there's no spare bit to
// mean "leave alone."
public record SetApplianceOfficerRequest(Guid? OfficerInChargeEmployeeId, string? OfficerInChargeName);

// Source is never a caller-supplied field: the integration API and the
// control room console both always write ControlRoom; only
// TabletIncidentsController's own note endpoint ever writes Crew.
public record AddIncidentUpdateRequest(
    string? AuthorName, string Text, IncidentUpdateType UpdateType = IncidentUpdateType.General,
    Guid? ReplyToUpdateId = null);

public record AddCrewNoteRequest(string Text, Guid? AuthorEmployeeId, Guid? ReplyToUpdateId = null);

// Same trust model as AddIncidentUpdateRequest.AuthorName -- the control
// room console already knows its own operator's display name from their
// session, Vision never calls this endpoint, so there's no case where a
// server-resolved identity is needed here that the caller doesn't already
// have.
public record AcknowledgeUpdateRequest(string? AcknowledgedByName);

// Only Text -- RaisedByName/RaisedByEmployeeId are resolved server-side by
// the caller (see IncidentObjective's own comment), not accepted here the
// way AddActionRequest.RaisedByName is. No Achieve/ReopenObjectiveRequest
// types exist: both endpoints take an empty POST body, same shape as
// TabletIncidentsController's own start-navigation endpoint.
public record AddObjectiveRequest(string Text);

// Kind isn't direction-locked -- both the control room and tablet can
// raise either kind, see IncidentAction's own comment. RaisedByName is
// caller-supplied, same trust model as AddIncidentUpdateRequest.AuthorName
// -- the control room console already knows its own operator's name.
public record AddActionRequest(
    IncidentActionKind Kind, string Text, string? RaisedByName = null,
    Guid? AssignedToEmployeeId = null, string? AssignedToName = null, Guid? SectorId = null);

public record AcknowledgeActionRequest(string? AcknowledgedByName);

// Status must be Completed or Declined -- the terminal states.
public record ResolveActionRequest(IncidentActionStatus Status, string? ResolvedByName = null);

public static class IncidentMapping
{
    public static IncidentDto ToDto(this Incident incident) => new(
        incident.Id, incident.ExternalReference, incident.IncidentType, incident.Description,
        incident.Address, incident.Latitude, incident.Longitude,
        incident.OrgUnitId, incident.OrgUnit?.Name ?? "", incident.Status,
        incident.StartedAtUtc, incident.ClosedAtUtc, incident.UpdatedAtUtc,
        incident.CloseTypeId, incident.CloseType?.Code, incident.CloseType?.Name,
        incident.CloseActionsTaken, incident.CloseOutcome,
        incident.Appliances.OrderBy(a => a.Callsign)
            .Select(a => new IncidentApplianceDto(
                a.Id, a.Callsign, a.Status, a.ResourceKind, a.SectorId,
                a.OfficerInChargeEmployeeId,
                a.OfficerInChargeEmployeeId is not null ? a.OfficerInChargeEmployee?.DisplayName : a.OfficerInChargeName,
                null, null, null, a.UpdatedAtUtc)).ToList(),
        incident.Updates.OrderBy(u => u.CreatedAtUtc)
            .Select(u => new IncidentUpdateDto(
                u.Id, u.Source, u.AuthorName, u.AuthorEmployeeId, u.Text, u.UpdateType,
                u.AcknowledgedAtUtc, u.AcknowledgedByName, u.ReplyToUpdateId, u.CreatedAtUtc)).ToList(),
        incident.Sectors.OrderBy(s => s.SortOrder)
            .Select(s => new IncidentSectorDto(
                s.Id, s.Name, s.SortOrder, s.ParentId,
                s.PersonInChargeEmployeeId,
                s.PersonInChargeEmployeeId is not null ? s.PersonInChargeEmployee?.DisplayName : s.PersonInChargeName))
            .ToList(),
        incident.Objectives.OrderBy(o => o.CreatedAtUtc)
            .Select(o => new IncidentObjectiveDto(
                o.Id, o.Text, o.Status, o.Source, o.RaisedByName, o.RaisedByEmployeeId,
                o.AchievedAtUtc, o.AchievedByName, o.CreatedAtUtc))
            .ToList(),
        incident.Actions.OrderBy(a => a.CreatedAtUtc)
            .Select(a => new IncidentActionDto(
                a.Id, a.Kind, a.Text, a.Status, a.Source, a.RaisedByName, a.RaisedByEmployeeId,
                a.AssignedToEmployeeId,
                a.AssignedToEmployeeId is not null ? a.AssignedToEmployee?.DisplayName : a.AssignedToName,
                a.SectorId, a.AcknowledgedAtUtc, a.AcknowledgedByName, a.ResolvedAtUtc, a.ResolvedByName, a.CreatedAtUtc))
            .ToList(),
        incident.Attachments.OrderByDescending(a => a.UploadedAtUtc)
            .Select(a => new IncidentAttachmentDto(
                a.Id, a.FileName, a.ContentType, a.SizeBytes, a.UploadedAtUtc,
                a.UploadedByEmployee?.DisplayName ?? a.UploadedByDevice?.Label))
            .ToList());

    public static IncidentSummaryDto ToSummaryDto(this Incident incident) => new(
        incident.Id, incident.ExternalReference, incident.IncidentType, incident.Address,
        incident.OrgUnitId, incident.OrgUnit?.Name ?? "", incident.Status,
        incident.StartedAtUtc, incident.UpdatedAtUtc);

    // Only the two "view" endpoints (control room + tablet Get) use this --
    // every write-action response still returns the plain ToDto() above.
    // A write's own response doesn't need to already carry fresh GPS
    // telemetry that wasn't part of what was just written; the next ~15s
    // poll picks it up, the same tolerance IsOnline/connectivity already
    // has elsewhere in this codebase. Re-maps Appliances after the fact
    // (records, so `with` is cheap) rather than duplicating ToDto's whole
    // mapping just to thread a lookup dictionary through it.
    public static async Task<IncidentDto> ToDtoWithLocationsAsync(this Incident incident, ApplicationDbContext db, CancellationToken ct = default)
    {
        var dto = incident.ToDto();

        var callsigns = incident.Appliances.Select(a => a.Callsign.ToUpperInvariant()).ToHashSet();
        var devices = await db.Devices.IgnoreQueryFilters()
            .Where(d => d.OrganisationId == incident.OrganisationId && d.Callsign != null)
            .ToListAsync(ct);
        var deviceByCallsign = devices
            .Where(d => callsigns.Contains(d.Callsign!.ToUpperInvariant()))
            .GroupBy(d => d.Callsign!.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var enrichedAppliances = dto.Appliances.Select(a =>
            deviceByCallsign.TryGetValue(a.Callsign.ToUpperInvariant(), out var device)
                ? a with { Latitude = device.CurrentLatitude, Longitude = device.CurrentLongitude, LocationUpdatedAtUtc = device.LocationUpdatedAtUtc }
                : a).ToList();

        return dto with { Appliances = enrichedAppliances };
    }
}
