using System.Collections.Concurrent;
using Itinero;
using Itinero.Exceptions;
using Itinero.Profiles;
using Microsoft.Extensions.Options;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Routing;

public record RoutePoint(double Latitude, double Longitude);

public record RouteResult(double DistanceMeters, double DurationSeconds, List<RoutePoint> Points);

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
            var start = router.Resolve(routeProfile, (float)fromLatitude, (float)fromLongitude, 250);
            var end = router.Resolve(routeProfile, (float)toLatitude, (float)toLongitude, 250);
            var route = router.Calculate(routeProfile, start, end);

            var points = route.Shape.Select(c => new RoutePoint(c.Latitude, c.Longitude)).ToList();
            return Task.FromResult<(RouteResult?, RouteFailureReason?)>(
                (new RouteResult(route.TotalDistance, route.TotalTime, points), null));
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
