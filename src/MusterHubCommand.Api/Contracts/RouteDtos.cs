using MusterHubCommand.Api.Routing;

namespace MusterHubCommand.Api.Contracts;

public record RoutePointDto(double Latitude, double Longitude);

// DistanceMeters is how far into the route this instruction applies --
// "how far to drive before the next banner change," not this instruction's
// own segment length.
public record RouteInstructionDto(string Text, double DistanceMeters);

// Available=false is an ordinary, expected outcome (no routerdb deployed,
// the device hasn't reported a GPS fix yet, the two points can't be
// resolved onto the loaded road network, or genuinely no path exists) --
// callers render UnavailableReason as explanatory text instead of a line
// on the map, not as an error state.
public record RouteResponseDto(
    bool Available, string? UnavailableReason,
    double? DistanceMeters, double? DurationSeconds, List<RoutePointDto>? Points,
    List<RouteInstructionDto>? Instructions);

public static class RouteResponseMapping
{
    public static RouteResponseDto ToUnavailableDto(this RouteFailureReason reason) => new(
        false,
        reason switch
        {
            RouteFailureReason.Unavailable => "Routing isn't set up for this deployment yet.",
            RouteFailureReason.CouldNotResolvePoint => "Couldn't find a road near the appliance or the incident.",
            RouteFailureReason.NoRouteFound => "No route could be found between the appliance and the incident.",
            _ => "Route unavailable.",
        },
        null, null, null, null);

    public static RouteResponseDto ToDto(this RouteResult result) => new(
        true, null, result.DistanceMeters, result.DurationSeconds,
        result.Points.Select(p => new RoutePointDto(p.Latitude, p.Longitude)).ToList(),
        result.Instructions.Select(i => new RouteInstructionDto(i.Text, i.DistanceMeters)).ToList());
}
