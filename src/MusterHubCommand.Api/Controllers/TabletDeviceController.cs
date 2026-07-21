using Microsoft.AspNetCore.Mvc;
using MusterHubCommand.Api.Contracts;

namespace MusterHubCommand.Api.Controllers;

// Just enough for the tablet to tell "no callsign configured" apart from
// "callsign set, nothing assigned right now" -- both look like an empty
// incidents list otherwise. Reads straight off the device token's own
// claims (DeviceAuthenticationHandler re-queries Devices fresh on every
// request), no database call needed here.
[Route("api/tablet/device")]
public class TabletDeviceController : DeviceControllerBase
{
    [HttpGet]
    public ActionResult<TabletDeviceDto> Get() => Ok(new TabletDeviceDto(DeviceCallsign));
}
