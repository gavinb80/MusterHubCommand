namespace MusterHubCommand.Api.Data.Entities;

// Command's whole permission model is one binary distinction -- can this
// person act as a Control Room Operator (create/edit/close incidents, push
// updates, provision/revoke devices) or not -- so this is a plain grant
// table, not Rota's generic Role/RolePermission/PermissionCode machinery:
// there's exactly one thing to grant, and building a composable
// permission-code system for a single permission is exactly the kind of
// abstraction CLAUDE.md's own guidance says not to reach for ahead of a
// second real need. Org-wide only for V1 -- a control room manages the
// whole service, not one station at a time.
public class CommandOperator : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DateTimeOffset GrantedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
