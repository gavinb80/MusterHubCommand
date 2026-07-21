using System.Collections.Concurrent;
using Itinero;
using Itinero.Exceptions;
using Itinero.LocalGeo;
using Itinero.Profiles;
using Microsoft.Extensions.Options;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Routing;

public record RoutePoint(double Latitude, double Longitude);

public record RouteInstruction(string Text, double DistanceMeters);

public record RouteResult(double DistanceMeters, double DurationSeconds, List<RoutePoint> Points, List<RouteInstruction> Instructions);

// Route "unavailable" (no routerdb loaded) is a distinct outcome from
// "no route exists" or "couldn't resolve a point on the road network" --
// all three are ordinary, expected results a caller renders differently,
// none of them are exceptions to propagate.
public enum RouteFailureReason { Unavailable, CouldNotResolvePoint, NoRouteFound }

// Wraps Itinero: loads one RouterDb once at startup (built offline from an
// OSM extract -- see docs/routing.md for the build script) and computes
// routes on demand using a vehicle profile derived from the appliance's own
// VehicleProfile row. VehicleProfile rows are created by Operators at
// runtime with arbitrary weight/height/width values, which the routerdb
// obviously can't have baked in at build time -- RouterDb.AddSupportedVehicle
// on an already-loaded RouterDb is exactly the escape hatch for that,
// confirmed live: a vehicle registered after deserializing still routes
// correctly and still respects its constraints.
public class RoutingService
{
    private readonly RouterDb? routerDb;
    private readonly Router? router;
    private readonly string luaTemplate;
    private readonly ILogger<RoutingService> logger;
    private readonly ConcurrentDictionary<string, DynamicVehicle> vehicleCache = new();
    private readonly Lock registerLock = new();

    public bool IsAvailable => router is not null;

    public RoutingService(IOptions<RoutingOptions> options, ILogger<RoutingService> logger)
    {
        this.logger = logger;
        luaTemplate = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Routing", "appliance.lua.template"));

        var configuredPath = options.Value.RouterDbPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            logger.LogWarning("Routing:RouterDbPath is unset -- appliance routing is unavailable.");
            return;
        }

        // A relative path (the dev fixture, App_Data/dev-tavistock.routerdb)
        // resolves against the app's own directory, the same as the Lua
        // template above -- an absolute path (prod's /home/data/command.routerdb,
        // which survives redeploys) is used as-is.
        var path = Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(AppContext.BaseDirectory, configuredPath);
        if (!File.Exists(path))
        {
            logger.LogWarning("Routing:RouterDbPath ('{Path}') does not exist -- appliance routing is unavailable.", path);
            return;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        routerDb = RouterDb.Deserialize(stream);
        router = new Router(routerDb);
        logger.LogInformation("Loaded routerdb from {Path}: {Vertices} vertices.", path, routerDb.Network.VertexCount);
    }

    public Task<(RouteResult? Result, RouteFailureReason? Failure)> ComputeRouteAsync(
        double fromLatitude, double fromLongitude, double toLatitude, double toLongitude, VehicleProfile? profile)
    {
        if (router is null) return Task.FromResult<(RouteResult?, RouteFailureReason?)>((null, RouteFailureReason.Unavailable));

        var vehicle = GetOrRegisterVehicle(profile);
        var routeProfile = vehicle.Fastest();

        try
        {
            var start = ResolveWithFallback(routeProfile, fromLatitude, fromLongitude);
            var end = ResolveWithFallback(routeProfile, toLatitude, toLongitude);
            var route = router.Calculate(routeProfile, start, end);

            var points = route.Shape.Select(c => new RoutePoint(c.Latitude, c.Longitude)).ToList();

            // Resolve snaps each endpoint to the nearest ROUTABLE road, which
            // for a point genuinely set back from any tagged road (a
            // driveway, a field entrance) can leave a small, real gap
            // between the route's own end and the true coordinate. A short
            // straight final/first segment closes that so the crew sees the
            // line actually reach the incident, not just the nearest road.
            // Deliberately bounded, though -- confirmed live at Grenofen
            // that a LARGE gap here means the underlying road data is
            // missing a real stretch of road (fixed by rebuilding the
            // routerdb from a fuller OSM extract, not papered over here),
            // and stretching a straight line across country to cover that
            // case draws a route that doesn't exist.
            const double maxBridgeMeters = 150;
            if (points.Count > 0)
            {
                var startGap = DistanceMeters(points[0].Latitude, points[0].Longitude, fromLatitude, fromLongitude);
                if (startGap is > 5 and <= maxBridgeMeters) points.Insert(0, new RoutePoint(fromLatitude, fromLongitude));
            }
            if (points.Count > 0)
            {
                var endGap = DistanceMeters(points[^1].Latitude, points[^1].Longitude, toLatitude, toLongitude);
                if (endGap is > 5 and <= maxBridgeMeters) points.Add(new RoutePoint(toLatitude, toLongitude));
            }

            var instructions = BuildInstructions(routeProfile, route);
            return Task.FromResult<(RouteResult?, RouteFailureReason?)>(
                (new RouteResult(route.TotalDistance, route.TotalTime, points, instructions), null));
        }
        catch (ResolveFailedException ex)
        {
            logger.LogInformation(ex, "Could not resolve a route endpoint onto the road network.");
            return Task.FromResult<(RouteResult?, RouteFailureReason?)>((null, RouteFailureReason.CouldNotResolvePoint));
        }
        catch (RouteNotFoundException ex)
        {
            logger.LogInformation(ex, "No route exists between the two resolved points.");
            return Task.FromResult<(RouteResult?, RouteFailureReason?)>((null, RouteFailureReason.NoRouteFound));
        }
    }

    // A tight 250m snap is right for the common case (an appliance sat at a
    // station forecourt, an incident on a mapped street) but too tight for
    // a rural incident set back from the nearest tagged road -- confirmed
    // live against the Tavistock extract: a Grenofen address resolved at
    // 1000m and nowhere below it. Only widens the search when the tight
    // radius genuinely fails, so town-centre snapping stays as precise as
    // before.
    private static readonly int[] ResolveRadiiMeters = [250, 1000, 5000];

    private RouterPoint ResolveWithFallback(Itinero.Profiles.IProfileInstance routeProfile, double latitude, double longitude)
    {
        for (var i = 0; i < ResolveRadiiMeters.Length; i++)
        {
            try
            {
                return router!.Resolve(routeProfile, (float)latitude, (float)longitude, ResolveRadiiMeters[i]);
            }
            catch (ResolveFailedException) when (i < ResolveRadiiMeters.Length - 1)
            {
            }
        }

        throw new ResolveFailedException($"Could not resolve point at [{latitude}, {longitude}] within {ResolveRadiiMeters[^1]}m.");
    }

    // Reuses Itinero's own estimator (already relied on in BuildInstructions
    // below) rather than a separate haversine implementation.
    private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2) =>
        Coordinate.DistanceEstimateInMeter([new Coordinate((float)lat1, (float)lon1), new Coordinate((float)lat2, (float)lon2)]);

    // Turn-by-turn text for the tablet's navigate mode. Best-effort: an
    // instruction-generation failure (e.g. Route.Shape too short, or a
    // future Itinero version changing behaviour) must never take down
    // routing itself, which is why this is a separate try/catch from the
    // one around Calculate above and simply degrades to no instructions.
    private List<RouteInstruction> BuildInstructions(Itinero.Profiles.Profile routeProfile, Itinero.Route route)
    {
        try
        {
            if (routeProfile.InstructionGenerator is null) return [];

            var instructions = routeProfile.InstructionGenerator.Generate(route, new HumanizedLanguageReference());
            return instructions.Select(instr =>
            {
                var upToHere = route.Shape.Take(instr.Shape + 1).ToList();
                var distance = upToHere.Count > 1 ? Coordinate.DistanceEstimateInMeter(upToHere) : 0;
                return new RouteInstruction(instr.Text, distance);
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Instruction generation failed for a route -- continuing without turn-by-turn text.");
            return [];
        }
    }

    // Keyed by the profile's actual constraint values, not just its Id --
    // an Operator editing a VehicleProfile's weight/height/width in Setup
    // must take effect on the next route request, not keep routing with
    // whatever was cached under that Id before the edit. The routerdb
    // itself is never pruned of superseded entries; a handful of stale,
    // unused vehicle registrations per organisation costs nothing worth
    // building eviction for.
    private DynamicVehicle GetOrRegisterVehicle(VehicleProfile? profile)
    {
        var key = profile is null
            ? "default"
            : $"{profile.Id}-{profile.MaxWeightTonnes}-{profile.MaxHeightMetres}-{profile.MaxWidthMetres}";

        return vehicleCache.GetOrAdd(key, k =>
        {
            var script = luaTemplate
                .Replace("__NAME__", $"appliance-{k}")
                .Replace("__MAX_WEIGHT__", LuaNumber(profile?.MaxWeightTonnes))
                .Replace("__MAX_HEIGHT__", LuaNumber(profile?.MaxHeightMetres))
                .Replace("__MAX_WIDTH__", LuaNumber(profile?.MaxWidthMetres));
            var vehicle = DynamicVehicle.Load(script);

            lock (registerLock)
            {
                if (!routerDb!.Supports(vehicle.Name)) routerDb.AddSupportedVehicle(vehicle);
            }
            return vehicle;
        });
    }

    private static string LuaNumber(double? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nil";
}
