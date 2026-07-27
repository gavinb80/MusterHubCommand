namespace MusterHubCommandTablet.Models;

// Mirrors MusterHubCommand.Api's Contracts/IncidentDtos.cs -- see that
// file's own comment for why these serialize as strings, not ordinal ints.

public record IncidentSummaryDto(
    Guid Id, string ExternalReference, string IncidentType, string? Address,
    Guid OrgUnitId, string OrgUnitName, string Status,
    DateTimeOffset StartedAtUtc, DateTimeOffset UpdatedAtUtc);

public record IncidentApplianceDto(
    Guid Id, string Callsign, string Status, string ResourceKind, Guid? SectorId,
    Guid? OfficerInChargeEmployeeId, string? OfficerInChargeName,
    double? Latitude, double? Longitude, DateTimeOffset? LocationUpdatedAtUtc, DateTimeOffset UpdatedAtUtc);

public record IncidentSectorDto(
    Guid Id, string Name, int SortOrder, Guid? ParentId,
    Guid? PersonInChargeEmployeeId, string? PersonInChargeName);

public record IncidentUpdateDto(
    Guid Id, string Source, string? AuthorName, Guid? AuthorEmployeeId,
    string Text, string UpdateType,
    DateTimeOffset? AcknowledgedAtUtc, string? AcknowledgedByName,
    Guid? ReplyToUpdateId, DateTimeOffset CreatedAtUtc);

// RaisedByName/AchievedByName are always the caller's own resolved
// identity -- see the API's own IncidentObjectiveDto comment.
public record IncidentObjectiveDto(
    Guid Id, string Text, string Status, string Source,
    string RaisedByName, Guid? RaisedByEmployeeId,
    DateTimeOffset? AchievedAtUtc, string? AchievedByName, DateTimeOffset CreatedAtUtc);

public record AddObjectiveRequest(string Text);

// AssignedToName/RaisedByName are always the display name -- resolved
// server-side the same way IncidentSectorDto.PersonInChargeName is.
public record IncidentActionDto(
    Guid Id, string Kind, string Text, string Status,
    string Source, string? RaisedByName, Guid? RaisedByEmployeeId,
    Guid? AssignedToEmployeeId, string? AssignedToName, Guid? SectorId,
    DateTimeOffset? AcknowledgedAtUtc, string? AcknowledgedByName,
    DateTimeOffset? ResolvedAtUtc, string? ResolvedByName, DateTimeOffset CreatedAtUtc);

public record IncidentDto(
    Guid Id, string ExternalReference, string IncidentType, string? Description,
    string? Address, double? Latitude, double? Longitude,
    Guid OrgUnitId, string OrgUnitName, string Status,
    DateTimeOffset StartedAtUtc, DateTimeOffset? ClosedAtUtc, DateTimeOffset UpdatedAtUtc,
    List<IncidentApplianceDto> Appliances, List<IncidentUpdateDto> Updates,
    List<IncidentSectorDto> Sectors, List<IncidentObjectiveDto> Objectives, List<IncidentActionDto> Actions,
    List<IncidentAttachmentDto> Attachments);

public record IncidentAttachmentDto(
    Guid Id, string FileName, string ContentType, long SizeBytes,
    DateTimeOffset UploadedAtUtc, string? UploadedByName);

public record AddCrewNoteRequest(string Text, Guid? AuthorEmployeeId, Guid? ReplyToUpdateId = null);

// Kind isn't direction-locked -- see the API's own AddActionRequest comment.
public record AddActionRequest(
    string Kind, string Text, string? RaisedByName = null,
    Guid? AssignedToEmployeeId = null, string? AssignedToName = null, Guid? SectorId = null);

public record ResolveActionRequest(string Status, string? ResolvedByName = null);

public record RoutePointDto(double Latitude, double Longitude);

public record RouteInstructionDto(string Text, double DistanceMeters);

public record RouteResponseDto(
    bool Available, string? UnavailableReason,
    double? DistanceMeters, double? DurationSeconds,
    List<RoutePointDto>? Points, List<RouteInstructionDto>? Instructions);

public record UpdateDeviceLocationRequest(double Latitude, double Longitude);

public record OrganisationSettingsDto(double GeofenceRadiusMeters);

public record TabletDeviceDto(string? Callsign);
