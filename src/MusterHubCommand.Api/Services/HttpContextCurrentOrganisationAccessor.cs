using System.Security.Claims;

namespace MusterHubCommand.Api.Services;

// Reads "org_id" regardless of which authentication scheme validated the
// request -- core's JWT carries it for the web console, and
// DeviceAuthenticationHandler stamps the same claim name from the paired
// Device's OrganisationId for the tablet, so downstream code (including
// ApplicationDbContext's tenant filter) never needs to know or care which
// one authenticated a given call.
public class HttpContextCurrentOrganisationAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentOrganisationAccessor
{
    public Guid OrganisationId
    {
        get
        {
            var claim = httpContextAccessor.HttpContext?.User.FindFirstValue("org_id");
            return claim is not null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
        }
    }
}
