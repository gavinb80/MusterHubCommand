// Builds a .routerdb file from an OSM extract (.osm XML or .osm.pbf) --
// see docs/routing.md for how to get the extract and when to re-run this.
//
// Usage: dotnet run -- <input.osm[.pbf]> <output.routerdb>
//
// Bakes in one unrestricted instance of Command's own appliance profile
// (Routing/appliance.lua.template) as the LoadOsmData-time vehicle --
// NOT Itinero's stock Car profile. This matters more than it looks: a
// vehicle's own profile_whitelist controls which OSM tags survive into the
// routerdb's stored edge data AT BUILD TIME, and car.lua's whitelist
// doesn't include maxweight/maxheight/maxwidth at all, so building against
// Car silently discards those tags before any appliance profile added
// later ever gets a chance to see them -- confirmed live: a post-hoc
// vehicle correctly detoured around a real maxheight=4 road when the
// baked-in vehicle used this template, and silently ignored the same
// restriction when the baked-in vehicle was Car. Every real
// VehicleProfile an Operator creates in Setup is still registered
// dynamically at runtime (RoutingService.cs) -- only the WHITELIST that
// determines which tags are even available to check has to come from a
// vehicle present at build time.

using Itinero;
using Itinero.IO.Osm;
using Itinero.Profiles;
using OsmSharp.Streams;

if (args.Length != 2)
{
    Console.WriteLine("Usage: dotnet run -- <input.osm[.pbf]> <output.routerdb>");
    return 1;
}

var (inputPath, outputPath) = (args[0], args[1]);
if (!File.Exists(inputPath))
{
    Console.WriteLine($"Input file not found: {inputPath}");
    return 1;
}

var templatePath = Path.Combine(AppContext.BaseDirectory, "appliance.lua.template");
var script = File.ReadAllText(templatePath)
    .Replace("__NAME__", "baseline-appliance")
    .Replace("__MAX_WEIGHT__", "nil")
    .Replace("__MAX_HEIGHT__", "nil")
    .Replace("__MAX_WIDTH__", "nil");
var baselineVehicle = DynamicVehicle.Load(script);

Console.WriteLine($"Loading {inputPath}...");
var routerDb = new RouterDb();
using (var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
{
    OsmStreamSource source = inputPath.EndsWith(".pbf", StringComparison.OrdinalIgnoreCase)
        ? new PBFOsmStreamSource(stream)
        : new XmlOsmStreamSource(stream);
    routerDb.LoadOsmData(source, baselineVehicle);
}

Console.WriteLine($"Vertices: {routerDb.Network.VertexCount}, Edges: {routerDb.Network.EdgeCount}");

using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
{
    routerDb.Serialize(outStream);
}
Console.WriteLine($"Wrote {outputPath}");
return 0;
