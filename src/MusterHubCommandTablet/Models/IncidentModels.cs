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

// RaisedByName/ReviewedByName are always the caller's own resolved identity
// -- see the API's own IncidentRiskDto comment. Unlike an objective,
// raising a risk also lands a Hazard entry in the Timeline.
public record IncidentRiskDto(
    Guid Id, string Description, string RiskLevel, string? ControlMeasure, string Status, string Source,
    string RaisedByName, Guid? RaisedByEmployeeId,
    DateTimeOffset? ReviewedAtUtc, string? ReviewedByName, DateTimeOffset CreatedAtUtc);

public record AddRiskRequest(string Description, string RiskLevel, string? ControlMeasure);

// Status isn't stored -- Overdue is computed server-side at read time, see
// the API's own BaWearerDto comment.
public record BaWearerDto(
    Guid Id, string Name, double CylinderPressureBar,
    DateTimeOffset EnteredAtUtc, DateTimeOffset WhistleAtUtc, DateTimeOffset? ExitedAtUtc, string Status);

public record BaTeamDto(
    Guid Id, string Name, string TeamLeader, string? CommsChannel, string? Briefing, string? Equipment,
    List<BaWearerDto> Wearers);

public record BaEntryControlPointDto(Guid Id, string Name, string Stage, bool IsOwnedByThisDevice, List<BaTeamDto> Teams);

public record AddBaEntryControlPointRequest(string Name, string Stage);

public record AddBaTeamRequest(string Name, string TeamLeader, string? CommsChannel = null, string? Briefing = null, string? Equipment = null);

// WhistleMinutes, not an absolute WhistleAtUtc -- resolved against the
// server's own clock, not this device's (which isn't trustworthy enough
// to compute a timestamp that gets persisted; Android emulators drift).
public record AddBaWearerRequest(string Name, double CylinderPressureBar, int WhistleMinutes);

// AssignedToName/RaisedByName are always the display name -- resolved
// server-side the same way IncidentSectorDto.PersonInChargeName is.
public record IncidentActionDto(
    Guid Id, string Kind, string Text, string Status,
    string Source, string? RaisedByName, Guid? RaisedByEmployeeId,
    Guid? AssignedToEmployeeId, string? AssignedToName, Guid? SectorId,
    DateTimeOffset? AcknowledgedAtUtc, string? AcknowledgedByName,
    DateTimeOffset? ResolvedAtUtc, string? ResolvedByName, DateTimeOffset CreatedAtUtc);

// ServerNowUtc -- see IncidentDetailViewModel.ServerNow's own comment:
// this device's clock isn't trustworthy enough on its own for BA Entry
// Control's live countdowns, so every response carries the server's own
// idea of "now" to correct against.
public record IncidentDto(
    Guid Id, string ExternalReference, string IncidentType, string? Description,
    string? Address, double? Latitude, double? Longitude,
    Guid OrgUnitId, string OrgUnitName, string Status,
    DateTimeOffset StartedAtUtc, DateTimeOffset? ClosedAtUtc, DateTimeOffset UpdatedAtUtc,
    List<IncidentApplianceDto> Appliances, List<IncidentUpdateDto> Updates,
    List<IncidentSectorDto> Sectors, List<IncidentObjectiveDto> Objectives, List<IncidentRiskDto> Risks,
    List<IncidentActionDto> Actions, List<BaEntryControlPointDto> BaEntryControlPoints, List<IncidentAttachmentDto> Attachments,
    DateTimeOffset ServerNowUtc);

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

public record OrganisationSettingsDto(double GeofenceRadiusMeters, bool BaEntryControlEnabled);

public record TabletDeviceDto(string? Callsign);
