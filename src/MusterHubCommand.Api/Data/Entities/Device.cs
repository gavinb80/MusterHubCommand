namespace MusterHubCommand.Api.Data.Entities;

// A paired appliance tablet's persistent identity -- deliberately not a
// per-user login (see docs/architecture-plan.md, the auth model note in the
// plan): the tablet is provisioned once against a station/appliance and
// stays signed in indefinitely, same "hash the secret, show the plaintext
// once, revoke by flag" shape as core's own ApiKeyEntity. Every Command API
// call from the tablet carries TokenHash's plaintext counterpart as a bearer
// token, validated here instead of against core's JWT/JWKS chain -- there is
// no human session to refresh, and forcing a persistent kiosk device through
// a 30-minute user token fights the grain.
public class Device : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public Guid OrgUnitId { get; set; }
    public OrgUnit? OrgUnit { get; set; }

    public required string Label { get; set; }
    public required string TokenHash { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}
