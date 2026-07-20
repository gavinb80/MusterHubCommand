using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// Setup's key management for Vision (or any other service's own system) --
// same shape as DevicesController: issue shows the plaintext once, revoke
// is a soft IsActive flip. Operator-gated: a live integration key is
// exactly as sensitive as a device pairing token, since either one can push
// incident data into this organisation.
[Route("api/integration-api-keys")]
public class IntegrationApiKeysController(
    ApplicationDbContext db,
    ICurrentOrganisationAccessor organisationAccessor,
    ICurrentEmployeeAccessor currentEmployeeAccessor,
    OperatorPermissionChecker operatorChecker)
    : CommandControllerBase(organisationAccessor, currentEmployeeAccessor, operatorChecker)
{
    [HttpGet]
    public async Task<ActionResult<List<IntegrationApiKeyDto>>> List()
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var keys = await db.IntegrationApiKeys.Where(k => k.OrganisationId == OrganisationId).OrderBy(k => k.Label).ToListAsync();
        return Ok(keys.Select(k => new IntegrationApiKeyDto(k.Id, k.Label, k.CreatedAtUtc, k.LastUsedAtUtc, k.IsActive)));
    }

    [HttpPost]
    public async Task<ActionResult<CreateIntegrationApiKeyResponse>> Create(CreateIntegrationApiKeyRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        if (string.IsNullOrWhiteSpace(request.Label)) return BadRequest("Label is required.");

        var plaintext = SecretHasher.GenerateToken();
        var key = new IntegrationApiKey
        {
            OrganisationId = OrganisationId,
            Label = request.Label.Trim(),
            KeyHash = SecretHasher.Hash(plaintext),
        };
        db.IntegrationApiKeys.Add(key);
        await db.SaveChangesAsync();

        return Ok(new CreateIntegrationApiKeyResponse(key.Id, key.Label, plaintext));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var key = await db.IntegrationApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.OrganisationId == OrganisationId);
        if (key is null) return NotFound();

        key.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
