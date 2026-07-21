namespace MusterHubCommand.Api.Data.Entities;

// An appliance class's physical routing constraints -- "Aerial Ladder
// Platform" might be 4.2m tall and 26 tonnes, "Light Pump" might have no
// real restriction at all. Assigned to a Device from Setup; RoutingService
// builds a real Itinero vehicle profile from these values (see
// Routing/appliance.lua.template) so a route genuinely avoids a
// height-restricted bridge or weight-limited road the appliance can't
// physically use, rather than just changing assumed speed like Itinero's
// own shipped "bigtruck" profile does.
public class VehicleProfile : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public required string Name { get; set; }

    // Null = no known restriction on this dimension -- the router only
    // rejects a way when both the way carries a real tag AND the
    // appliance's own value for that dimension exceeds it, so leaving
    // these unset is safe (behaves like an ordinary car/HGV profile).
    public double? MaxWeightTonnes { get; set; }
    public double? MaxHeightMetres { get; set; }
    public double? MaxWidthMetres { get; set; }
}
