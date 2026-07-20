namespace MusterHubCommand.Api.Data.Entities;

// Opt-in nightly directory sync -- identical shape to Rota/Skills' own
// config. Storing the core directory key is what makes a scheduled job
// possible; the blast radius is bounded (ImportAsync rejects a key for any
// other service) and removable by turning the sync off, which deletes the row.
public class CoreDirectorySyncConfig : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public required string ApiKey { get; set; }
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
    // "3 units, 42 employees" on success; the error message on failure.
    public string? LastSyncNote { get; set; }
}
