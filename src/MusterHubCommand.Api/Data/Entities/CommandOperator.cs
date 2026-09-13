namespace MusterHubCommand.Api.Data.Entities;

// Command's permission model is three concrete tiers, not Rota's generic
// Role/RolePermission/PermissionCode machinery -- ControlRoom and
// CommandSupport are named distinctly (different real-world positions --
// Fire Control desk staff vs. on-scene admin/logging support for the IC)
// even though they carry the same baseline permission today, and
// IncidentCommander is the one elevated tier: cancel incidents, manage
// sectors/hierarchy, grant/revoke operators. Building a composable
// N-tier permission-code system for three fixed, named tiers is exactly
// the kind of abstraction CLAUDE.md's own guidance says not to reach for
// ahead of a real need for more. Row presence still means "is an operator
// at all"; Tier says which one. Org-wide only for V1 -- a control room
// manages the whole service, not one station at a time.
public class CommandOperator : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public CommandOperatorTier Tier { get; set; } = CommandOperatorTier.ControlRoom;

    public DateTimeOffset GrantedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public enum CommandOperatorTier
{
    ControlRoom,
    CommandSupport,
    IncidentCommander,
}
