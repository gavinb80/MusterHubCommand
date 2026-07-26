namespace MusterHubCommand.Api.Data.Entities;

// A control-room-defined node in one incident's command tree -- scoped to
// the incident it's created on, not a reusable org-wide concept, since a
// node's name only makes sense in the context of the incident it
// describes. Despite the name (kept from when this was flat sectors only,
// to avoid rippling a rename through the service/controller/tests/UI that
// already shipped), a node can represent anything in the "Incident
// Hierarchy" screen's tree -- an incident commander, a command unit, a
// sector, a sector commander, or a resource -- distinguished only by where
// it sits (ParentId) and whether an IncidentAppliance points at it, not by
// a separate "kind" field. An appliance with no SectorId is "Unassigned",
// not a node of its own.
public class IncidentSector
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public required string Name { get; set; }
    public int SortOrder { get; set; }

    // Null = a root of the tree (today's flat sectors are all roots with
    // no children -- a one-level-deep case of the same model, not a
    // separate thing).
    public Guid? ParentId { get; set; }
    public IncidentSector? Parent { get; set; }

    // Same dual-mode pattern as IncidentUpdate.AuthorEmployeeId/AuthorName:
    // set PersonInChargeEmployeeId when Control picks a real person from
    // the org's employee list, fall back to plain PersonInChargeName when
    // they just type someone who isn't in it.
    public Guid? PersonInChargeEmployeeId { get; set; }
    public Employee? PersonInChargeEmployee { get; set; }
    public string? PersonInChargeName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
