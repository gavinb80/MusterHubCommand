namespace MusterHubCommand.Api.Data.Entities;

// The organisation hierarchy's *levels* are entirely org-defined, not a fixed
// enum -- mirrors core's own Group/Station shape (Command only ever imports
// those two levels; it has no concept of a watch, unlike Rota).
public class OrgUnitType : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public required string Code { get; set; }
    public required string Name { get; set; }

    public Guid? AllowedParentTypeId { get; set; }
    public OrgUnitType? AllowedParentType { get; set; }

    public int SortOrder { get; set; }
}
