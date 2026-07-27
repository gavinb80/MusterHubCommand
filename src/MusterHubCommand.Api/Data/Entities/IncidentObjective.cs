namespace MusterHubCommand.Api.Data.Entities;

public enum IncidentObjectiveStatus
{
    Open,
    Achieved,
}

// A structured "what are we actually trying to achieve" list, deliberately
// distinct from the Timeline -- IncidentService's RaiseObjectiveAsync/
// AchieveObjectiveAsync/ReopenObjectiveAsync never call
// QueueActionChangeUpdate or otherwise write an IncidentUpdate, unlike
// every IncidentAction status change. Two states, not IncidentAction's
// four, and no Kind/assignee/sector: an objective belongs to the incident
// as a whole, not a person, appliance or category.
public class IncidentObjective
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public required string Text { get; set; }
    public IncidentObjectiveStatus Status { get; set; } = IncidentObjectiveStatus.Open;

    public IncidentUpdateSource Source { get; set; }

    // Always resolved server-side from the caller's own session (web: the
    // operator's Employee.DisplayName; tablet: DeviceCallsign), never
    // caller-supplied -- unlike IncidentAction.RaisedByName, an
    // objective's author is never a free-text/typeahead decision, just
    // "whoever's logged into this session". RaisedByEmployee is audit-only:
    // never .Include()'d or used for display, since RaisedByName is
    // already the string to show.
    public required string RaisedByName { get; set; }
    public Guid? RaisedByEmployeeId { get; set; }
    public Employee? RaisedByEmployee { get; set; }

    // Cleared, not left alone, when ReopenObjectiveAsync flips Status back
    // to Open -- so a later re-Achieve stamps a fresh time/name, not the
    // original achiever's.
    public DateTimeOffset? AchievedAtUtc { get; set; }
    public string? AchievedByName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
