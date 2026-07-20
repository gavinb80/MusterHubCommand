using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Data;

namespace MusterHubCommand.Api.Services;

// Validates the X-Api-Key header on IntegrationIncidentsController's
// endpoints -- same manual-validation shape as core's own integration
// controllers and Rota's IntegrationShiftsController, except this key is
// per-organisation and database-issued (see IntegrationApiKey) rather than
// a single shared appsettings secret, since Command is one deployment
// serving many services' own Vision instances.
public class IntegrationApiKeyValidator(ApplicationDbContext db)
{
    public async Task<Guid?> ValidateAsync(string? providedKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providedKey)) return null;

        var hash = SecretHasher.Hash(providedKey);
        var key = await db.IntegrationApiKeys.IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive, ct);
        if (key is null) return null;

        key.LastUsedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return key.OrganisationId;
    }
}
