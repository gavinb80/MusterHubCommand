using Microsoft.AspNetCore.Mvc;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// Backs the "Locate" button on the manual New Incident form. Operator-gated
// like every other web console endpoint -- the tablet and the Vision
// integration API never need this, since a tablet only reads incidents and
// Vision already sends real coordinates.
[Route("api/geocode")]
public class GeocodeController(
    GeocodingService geocodingService,
    ICurrentOrganisationAccessor organisationAccessor,
    ICurrentEmployeeAccessor currentEmployeeAccessor,
    OperatorPermissionChecker operatorChecker)
    : CommandControllerBase(organisationAccessor, currentEmployeeAccessor, operatorChecker)
{
    [HttpGet]
    public async Task<ActionResult<GeocodeResponseDto>> Geocode([FromQuery] string query)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        if (string.IsNullOrWhiteSpace(query)) return Ok(new GeocodeResponseDto(false, null, null, null));

        var result = await geocodingService.GeocodeAsync(query.Trim());
        return Ok(result is null
            ? new GeocodeResponseDto(false, null, null, null)
            : new GeocodeResponseDto(true, result.Latitude, result.Longitude, result.DisplayName));
    }
}
