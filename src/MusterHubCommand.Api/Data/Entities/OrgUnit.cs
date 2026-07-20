namespace MusterHubCommand.Api.Data.Entities;

// A node in the org hierarchy (Group/Station, mirrored from core). Path is a
// Postgres ltree materialized path, auto-maintained on insert by
// OrgUnitPathInterceptor -- same mechanism as Rota/Skills' own OrgUnit.
public class OrgUnit : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public Guid OrgUnitTypeId { get; set; }
    public OrgUnitType? OrgUnitType { get; set; }

    public Guid? ParentId { get; set; }
    public OrgUnit? Parent { get; set; }

    public required string Name { get; set; }
    public string? Code { get; set; }

    public string Path { get; set; } = "";

    public bool IsActive { get; set; } = true;

    // Opaque reference to the core entity (Group.Id / Station.Id) this unit
    // was imported from -- the idempotency key for re-runs of the core
    // directory import, same match-by-external-reference rule core's own
    // import endpoints follow.
    public Guid? CoreId { get; set; }
}
