namespace MusterHubCommand.Api.Data.Entities;

public enum IncidentUpdateSource
{
    ControlRoom,
    Crew,
}

public enum IncidentUpdateType
{
    General,
    Hazard,
    ResourceChange,
    Note,
    // Auto-logged when an IncidentAction changes status -- same role
    // ResourceChange plays for appliance status, so the Timeline stays
    // the one "what happened" feed rather than a second, disconnected
    // log. Not acknowledgeable, same rule as ResourceChange.
    ActionChange,
}

// The live timeline -- control-room-pushed updates and crew-entered notes
// are the same concept, distinguished by Source. Append-only: nothing edits
// or deletes a row once written, so the timeline a crew saw on scene can
// never be rewritten after the fact.
public class IncidentUpdate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public IncidentUpdateSource Source { get; set; }

    // Free text for a ControlRoom update (Vision doesn't send an
    // employee identity for the person keying it in); AuthorEmployeeId for
    // a Crew note when whoever's writing chooses to identify themselves --
    // the device itself is shared, so identifying the author is optional.
    public string? AuthorName { get; set; }
    public Guid? AuthorEmployeeId { get; set; }

    public required string Text { get; set; }
    public IncidentUpdateType UpdateType { get; set; } = IncidentUpdateType.General;

    // Null = not yet acknowledged. Only meaningful for General/Hazard --
    // a ResourceChange line is a status log entry, not something anyone
    // needs to confirm they've seen.
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
    public string? AcknowledgedByName { get; set; }

    // Either side replying to a specific prior message -- thickens this
    // same append-only Timeline into a lightweight thread rather than
    // introducing a separate conversation view. Never itself replied-to
    // recursively deep in practice, but nothing stops it.
    public Guid? ReplyToUpdateId { get; set; }
    public IncidentUpdate? ReplyToUpdate { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
