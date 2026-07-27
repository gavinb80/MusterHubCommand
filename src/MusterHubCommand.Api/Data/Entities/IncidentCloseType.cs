namespace MusterHubCommand.Api.Data.Entities;

// The organisation's own close-out classification scheme (e.g. "M1.2.3 -
// Fire In Open") -- org-defined, not a fixed enum, same reasoning as
// OrgUnitType: different brigades use different coding conventions, so this
// is a managed lookup (Setup > Close Types), not baked into the app.
public class IncidentCloseType : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public required string Code { get; set; }
    public required string Name { get; set; }
}
