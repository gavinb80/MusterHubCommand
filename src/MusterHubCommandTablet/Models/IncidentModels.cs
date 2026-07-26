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
    DateTimeOffset? AcknowledgedAtUtc, string? AcknowledgedByName, DateTimeOffset CreatedAtUtc);

public record IncidentDto(
    Guid Id, string ExternalReference, string IncidentType, string? Description,
    string? Address, double? Latitude, double? Longitude,
    Guid OrgUnitId, string OrgUnitName, string Status,
    DateTimeOffset StartedAtUtc, DateTimeOffset? ClosedAtUtc, DateTimeOffset UpdatedAtUtc,
    List<IncidentApplianceDto> Appliances, List<IncidentUpdateDto> Updates, List<IncidentSectorDto> Sectors);

public record AddCrewNoteRequest(string Text, Guid? AuthorEmployeeId);

public record RoutePointDto(double Latitude, double Longitude);

public record RouteInstructionDto(string Text, double DistanceMeters);

public record RouteResponseDto(
    bool Available, string? UnavailableReason,
    double? DistanceMeters, double? DurationSeconds,
    List<RoutePointDto>? Points, List<RouteInstructionDto>? Instructions);

public record UpdateDeviceLocationRequest(double Latitude, double Longitude);

public record OrganisationSettingsDto(double GeofenceRadiusMeters);

public record TabletDeviceDto(string? Callsign);
