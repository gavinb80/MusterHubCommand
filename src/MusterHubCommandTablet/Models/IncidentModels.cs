namespace MusterHubCommandTablet.Models;

// Mirrors MusterHubCommand.Api's Contracts/IncidentDtos.cs -- see that
// file's own comment for why these serialize as strings, not ordinal ints.

public record IncidentSummaryDto(
    Guid Id, string ExternalReference, string IncidentType, string? Address,
    Guid OrgUnitId, string OrgUnitName, string Status,
    DateTimeOffset StartedAtUtc, DateTimeOffset UpdatedAtUtc);

public record IncidentApplianceDto(Guid Id, string Callsign, string Status, DateTimeOffset UpdatedAtUtc);

public record IncidentUpdateDto(
    Guid Id, string Source, string? AuthorName, Guid? AuthorEmployeeId,
    string Text, string UpdateType, DateTimeOffset CreatedAtUtc);

public record IncidentDto(
    Guid Id, string ExternalReference, string IncidentType, string? Description,
    string? Address, double? Latitude, double? Longitude,
    Guid OrgUnitId, string OrgUnitName, string Status,
    DateTimeOffset StartedAtUtc, DateTimeOffset? ClosedAtUtc, DateTimeOffset UpdatedAtUtc,
    List<IncidentApplianceDto> Appliances, List<IncidentUpdateDto> Updates);

public record AddCrewNoteRequest(string Text, Guid? AuthorEmployeeId);

public record RoutePointDto(double Latitude, double Longitude);

public record RouteInstructionDto(string Text, double DistanceMeters);

public record RouteResponseDto(
    bool Available, string? UnavailableReason,
    double? DistanceMeters, double? DurationSeconds,
    List<RoutePointDto>? Points, List<RouteInstructionDto>? Instructions);

public record UpdateDeviceLocationRequest(double Latitude, double Longitude);
