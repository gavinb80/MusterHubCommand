using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusterHubCommand.Api.Data;

namespace MusterHubCommand.Api.Services;

public class DeviceAuthenticationSchemeOptions : AuthenticationSchemeOptions;

// A second, parallel authentication scheme alongside core's JWT bearer --
// the tablet app carries a long-lived Device token (Authorization: Bearer
// <token>, same header shape as a JWT so the client code doesn't need to
// know it's a different mechanism) instead of a core-issued session. This
// handler validates it against the Device table (hash compare, IsActive
// check) and builds a ClaimsPrincipal carrying "org_id" -- the same claim
// name core's JWT uses -- so HttpContextCurrentOrganisationAccessor and the
// tenant query filter work identically regardless of which scheme
// authenticated the request. A "device_id" claim lets controllers resolve
// which station/appliance is asking without a second lookup.
public class DeviceAuthenticationHandler(
    IOptionsMonitor<DeviceAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApplicationDbContext db)
    : AuthenticationHandler<DeviceAuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Device";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValue))
            return AuthenticateResult.NoResult();

        var header = headerValue.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0) return AuthenticateResult.Fail("Empty device token.");

        var hash = SecretHasher.Hash(token);
        // IgnoreQueryFilters: the tenant filter itself depends on
        // OrganisationId, which is exactly what resolving this device tells
        // us -- there's no organisation in scope yet at this point.
        var device = await db.Devices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.TokenHash == hash && d.IsActive);
        if (device is null) return AuthenticateResult.Fail("Invalid or revoked device token.");

        device.LastSeenAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim("org_id", device.OrganisationId.ToString()),
            new Claim("device_id", device.Id.ToString()),
            new Claim("org_unit_id", device.OrgUnitId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
