namespace MusterHubCommand.Api.Data.Entities;

// Task and resource request are the same shape -- a directed ask with a
// status lifecycle and an assignee/requester -- so they're one entity
// with a Kind flag rather than two parallel models. Kind isn't
// direction-locked (a Task is usually Control->Crew, a ResourceRequest
// usually Crew->Control): both the control-room and tablet APIs can
// raise either kind, same as this codebase generally prefers flexible
// free text over rigid enforcement (IncidentType is just a string).
public enum IncidentActionKind
{
    Task,
    ResourceRequest,
}

public enum IncidentActionStatus
{
    Open,
    Acknowledged,
    Completed,
    Declined,
}

public class IncidentAction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public IncidentActionKind Kind { get; set; }
    public required string Text { get; set; }
    public IncidentActionStatus Status { get; set; } = IncidentActionStatus.Open;

    public IncidentUpdateSource Source { get; set; }
    public string? RaisedByName { get; set; }
    public Guid? RaisedByEmployeeId { get; set; }

    // Dual-mode, same pattern as IncidentSector.PersonInCharge* --
    // employee link when picked from the typeahead, free text otherwise.
    public Guid? AssignedToEmployeeId { get; set; }
    public Employee? AssignedToEmployee { get; set; }
    public string? AssignedToName { get; set; }

    // Optional, mirrors IncidentAppliance.SectorId -- not every task or
    // request is tied to a specific sector.
    public Guid? SectorId { get; set; }
    public IncidentSector? Sector { get; set; }

    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
    public string? AcknowledgedByName { get; set; }

    // Completed or Declined -- the terminal state, whichever it lands on.
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public string? ResolvedByName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
