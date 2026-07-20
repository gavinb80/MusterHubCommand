namespace MusterHubCommand.Api.Data.Entities;

public enum IncidentStatus
{
    Open,
    Closed,
    Cancelled,
}

// Deliberately separate from core's own thin IncidentEntity (wellbeing
// follow-ups, Skills evidence linking) -- that keeps doing its own job
// untouched. This is Vision's incident, mirrored here for the on-scene
// tablet and control room's own use. ExternalReference is Vision's incident
// ID: the idempotency key every integration endpoint upserts against, same
// "match by unique external reference" convention as every other import
// endpoint in this codebase.
public class Incident : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public required string ExternalReference { get; set; }

    public required string IncidentType { get; set; }
    public string? Description { get; set; }

    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Which station's tablets/crew see this incident.
    public Guid OrgUnitId { get; set; }
    public OrgUnit? OrgUnit { get; set; }

    public IncidentStatus Status { get; set; } = IncidentStatus.Open;

    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    // Drives the tablet's poll comparison -- bumped on every field change,
    // every appliance replace, and every update append, so a plain "has this
    // incident changed since I last asked" check never needs a separate
    // change-log table.
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<IncidentAppliance> Appliances { get; set; } = [];
    public List<IncidentUpdate> Updates { get; set; } = [];
}
