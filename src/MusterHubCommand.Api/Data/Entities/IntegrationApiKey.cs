namespace MusterHubCommand.Api.Data.Entities;

// One organisation's key for pushing incident data in from Vision (or any
// other service's equivalent system) -- an Operator issues it from Setup,
// the plaintext is shown once, and IntegrationIncidentsController validates
// every inbound call against KeyHash. Same "hash the secret, show it once,
// revoke by flag" shape as Device and core's own ApiKeyEntity. Deliberately
// NOT ITenantScoped: the whole point of this table is being queried by
// AllowAnonymous integration endpoints that have no ambient organisation
// yet -- OrganisationId is what a successful lookup here resolves, not
// something to filter by beforehand.
public class IntegrationApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public required string Label { get; set; }
    public required string KeyHash { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}
