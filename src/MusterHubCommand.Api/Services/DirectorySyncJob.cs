using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Data;

namespace MusterHubCommand.Api.Services;

// Nightly re-import for every org that opted in: keeps stations and people
// current without an admin remembering to press the button. Per-org
// failures are captured and recorded on the config row, and the loop keeps
// going -- one org's expired key must not stall everyone else's sync.
public class DirectorySyncJob(ApplicationDbContext db, CoreDirectoryImportService importService, ILogger<DirectorySyncJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var configs = await db.CoreDirectorySyncConfigs.IgnoreQueryFilters().ToListAsync(cancellationToken);
        foreach (var config in configs)
        {
            try
            {
                var summary = await importService.ImportAsync(config.OrganisationId, config.ApiKey, cancellationToken);
                config.LastSyncNote =
                    $"{summary.UnitsCreated + summary.UnitsUpdated} units, {summary.EmployeesCreated + summary.EmployeesUpdated} employees";
                logger.LogInformation("Directory sync for organisation {OrganisationId}: {Note}", config.OrganisationId, config.LastSyncNote);
            }
            catch (Exception ex)
            {
                SentrySdk.CaptureException(ex);
                config.LastSyncNote = $"Failed: {ex.Message}";
                logger.LogWarning(ex, "Directory sync failed for organisation {OrganisationId}", config.OrganisationId);
            }
            config.LastSyncedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
