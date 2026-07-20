using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// The tablet app's base -- Device-token only. A tablet is never asked to
// hold a core JWT (see DeviceAuthenticationHandler for why), so this is a
// deliberately separate scheme from CommandControllerBase's JWT-bearer,
// not a shared "either works" policy: an endpoint means one specific kind
// of caller, never both.
[ApiController]
[Authorize(AuthenticationSchemes = DeviceAuthenticationHandler.SchemeName)]
public abstract class DeviceControllerBase : ControllerBase
{
    protected Guid OrganisationId => Guid.Parse(User.FindFirstValue("org_id")!);
    protected Guid DeviceOrgUnitId => Guid.Parse(User.FindFirstValue("org_unit_id")!);
    protected Guid DeviceId => Guid.Parse(User.FindFirstValue("device_id")!);
}
