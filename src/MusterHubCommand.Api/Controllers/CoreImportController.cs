using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

public record RunCoreImportRequest(string ApiKey);
public record SaveSyncConfigRequest(string ApiKey);
public record SyncConfigDto(bool Enabled, DateTimeOffset? LastSyncedAtUtc, string? LastSyncNote);

// Pulls the caller's own organisation's directory out of core and upserts
// it here -- same shape as Rota/Skills' own CoreImportController. The API
// key is used for the one outbound call and never stored (except by
// SaveSyncConfig, for the nightly job, which is the point of that call).
[Route("api/import")]
public class CoreImportController(
    ApplicationDbContext db,
    CoreDirectoryImportService importService,
    ICurrentOrganisationAccessor organisationAccessor,
    ICurrentEmployeeAccessor currentEmployeeAccessor,
    OperatorPermissionChecker operatorChecker)
    : CommandControllerBase(organisationAccessor, currentEmployeeAccessor, operatorChecker)
{
    [HttpPost("core-directory")]
    public async Task<ActionResult<CoreDirectoryImportSummary>> Run(RunCoreImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey)) return BadRequest("ApiKey is required.");
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        try
        {
            return Ok(await importService.ImportAsync(OrganisationId, request.ApiKey.Trim()));
        }
        catch (CoreDirectoryAuthException ex) { return BadRequest(ex.Message); }
        catch (CoreDirectoryMismatchException ex) { return BadRequest(ex.Message); }
        catch (HttpRequestException)
        {
            return Problem("Couldn't reach MusterHub to fetch the directory. Try again shortly.", statusCode: 502);
        }
    }

    [HttpGet("sync-config")]
    public async Task<ActionResult<SyncConfigDto>> GetSyncConfig()
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        var config = await db.CoreDirectorySyncConfigs.FirstOrDefaultAsync();
        return Ok(config is null
            ? new SyncConfigDto(false, null, null)
            : new SyncConfigDto(true, config.LastSyncedAtUtc, config.LastSyncNote));
    }

    // Storing the key is what makes the nightly job possible -- validated
    // by running an import RIGHT NOW, so a bad key never gets saved.
    [HttpPut("sync-config")]
    public async Task<ActionResult<CoreDirectoryImportSummary>> SaveSyncConfig(SaveSyncConfigRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey)) return BadRequest("ApiKey is required.");
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        CoreDirectoryImportSummary summary;
        try
        {
            summary = await importService.ImportAsync(OrganisationId, request.ApiKey.Trim());
        }
        catch (CoreDirectoryAuthException ex) { return BadRequest(ex.Message); }
        catch (CoreDirectoryMismatchException ex) { return BadRequest(ex.Message); }
        catch (HttpRequestException)
        {
            return Problem("Couldn't reach MusterHub to fetch the directory. Try again shortly.", statusCode: 502);
        }

        var config = await db.CoreDirectorySyncConfigs.FirstOrDefaultAsync();
        if (config is null)
        {
            config = new CoreDirectorySyncConfig { OrganisationId = OrganisationId, ApiKey = request.ApiKey.Trim() };
            db.CoreDirectorySyncConfigs.Add(config);
        }
        else
        {
            config.ApiKey = request.ApiKey.Trim();
        }
        config.LastSyncedAtUtc = DateTimeOffset.UtcNow;
        config.LastSyncNote =
            $"{summary.UnitsCreated + summary.UnitsUpdated} units, {summary.EmployeesCreated + summary.EmployeesUpdated} employees";
        await db.SaveChangesAsync();
        return Ok(summary);
    }

    [HttpDelete("sync-config")]
    public async Task<IActionResult> DisableSync()
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        var config = await db.CoreDirectorySyncConfigs.FirstOrDefaultAsync();
        if (config is not null)
        {
            db.CoreDirectorySyncConfigs.Remove(config);
            await db.SaveChangesAsync();
        }
        return NoContent();
    }
}
