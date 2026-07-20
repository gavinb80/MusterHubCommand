using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Contracts;

public record IncidentApplianceDto(Guid Id, string Callsign, ApplianceStatus Status, DateTimeOffset UpdatedAtUtc);

public record IncidentUpdateDto(
    Guid Id, IncidentUpdateSource Source, string? AuthorName, Guid? AuthorEmployeeId,
    string Text, IncidentUpdateType UpdateType, DateTimeOffset CreatedAtUtc);

public record IncidentDto(
    Guid Id, string ExternalReference, string IncidentType, string? Description,
    string? Address, double? Latitude, double? Longitude,
    Guid OrgUnitId, string OrgUnitName, IncidentStatus Status,
    DateTimeOffset StartedAtUtc, DateTimeOffset? ClosedAtUtc, DateTimeOffset UpdatedAtUtc,
    List<IncidentApplianceDto> Appliances, List<IncidentUpdateDto> Updates);

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

public record SetApplianceEntry(string Callsign, ApplianceStatus Status);
public record SetAppliancesRequest(List<SetApplianceEntry> Appliances);

// Source is never a caller-supplied field: the integration API and the
// control room console both always write ControlRoom; only
// TabletIncidentsController's own note endpoint ever writes Crew.
public record AddIncidentUpdateRequest(string? AuthorName, string Text, IncidentUpdateType UpdateType = IncidentUpdateType.General);

public record AddCrewNoteRequest(string Text, Guid? AuthorEmployeeId);

public static class IncidentMapping
{
    public static IncidentDto ToDto(this Incident incident) => new(
        incident.Id, incident.ExternalReference, incident.IncidentType, incident.Description,
        incident.Address, incident.Latitude, incident.Longitude,
        incident.OrgUnitId, incident.OrgUnit?.Name ?? "", incident.Status,
        incident.StartedAtUtc, incident.ClosedAtUtc, incident.UpdatedAtUtc,
        incident.Appliances.OrderBy(a => a.Callsign)
            .Select(a => new IncidentApplianceDto(a.Id, a.Callsign, a.Status, a.UpdatedAtUtc)).ToList(),
        incident.Updates.OrderBy(u => u.CreatedAtUtc)
            .Select(u => new IncidentUpdateDto(u.Id, u.Source, u.AuthorName, u.AuthorEmployeeId, u.Text, u.UpdateType, u.CreatedAtUtc)).ToList());

    public static IncidentSummaryDto ToSummaryDto(this Incident incident) => new(
        incident.Id, incident.ExternalReference, incident.IncidentType, incident.Address,
        incident.OrgUnitId, incident.OrgUnit?.Name ?? "", incident.Status,
        incident.StartedAtUtc, incident.UpdatedAtUtc);
}
