using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MusterHubCommand.Api.Tests;

// Replaces the JWKS-backed JwtBearer scheme in tests: identity comes from
// plain request headers instead of a signed token, so the suite exercises
// everything *after* signature validation (org scoping, entitlement policy,
// operator bootstrapping, tenant isolation) without needing a running core
// instance to mint real tokens. Same pattern as Rota/Skills' own
// TestAuthHandler. Only stands in for the web console's JWT scheme -- the
// Device scheme and X-Api-Key integration auth are both fully self-
// contained (a DB lookup, nothing external), so tests exercise those for
// real rather than faking them too.
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string OrgIdHeader = "X-Test-Org-Id";
    public const string PersonIdHeader = "X-Test-Person-Id";
    public const string NoEntitlementHeader = "X-Test-No-Entitlement";
    public const string ModuleAdminHeader = "X-Test-Module-Admin";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No org header = unauthenticated request; the API should 401.
        if (!Request.Headers.TryGetValue(OrgIdHeader, out var orgId) || string.IsNullOrEmpty(orgId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("org_id", orgId!) };

        if (Request.Headers.TryGetValue(PersonIdHeader, out var personId) && !string.IsNullOrEmpty(personId))
            claims.Add(new Claim(ClaimTypes.NameIdentifier, personId!));

        // Mirrors core's entitlements claim; omitted when a test wants to
        // prove RequireCommandEntitlement rejects an identity whose org
        // hasn't bought the module.
        if (!Request.Headers.ContainsKey(NoEntitlementHeader))
            claims.Add(new Claim("entitlements", "command"));

        // Mirrors core's module_admin claim (see MusterHub.Api's
        // UserModuleAdmin / Admin Users Edit page) -- set when a test wants
        // to prove EnsureModuleAdminGrantedAsync's auto-provisioning.
        if (Request.Headers.ContainsKey(ModuleAdminHeader))
            claims.Add(new Claim("module_admin", "command"));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
