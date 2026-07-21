using Itinero.LocalGeo;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Services;

// Runs on every tablet location report (TabletLocationController.Update) --
// not gated behind navigate mode, so an appliance that never tapped "Start
// navigation" still auto-arrives via the normal 60s background reports.
public class GeofenceService(ApplicationDbContext db, IncidentService incidentService)
{
    private const double DefaultGeofenceRadiusMeters = 50;

    public async Task CheckAndMarkOnSceneAsync(
        Guid organisationId, Guid orgUnitId, string? callsign, double latitude, double longitude, CancellationToken ct = default)
    {
        // Nothing to match this device's own actions against -- same
        // "needs a Callsign set in Setup" boundary as start-navigation.
        if (string.IsNullOrWhiteSpace(callsign)) return;

        var settings = await db.OrganisationSettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.OrganisationId == organisationId, ct);
        var radiusMeters = settings?.GeofenceRadiusMeters ?? DefaultGeofenceRadiusMeters;

        var openIncidents = await incidentService.ListAsync(organisationId, orgUnitId, activeOnly: true, ct);
        var here = new Coordinate((float)latitude, (float)longitude);

        foreach (var incident in openIncidents)
        {
            if (incident.Latitude is null || incident.Longitude is null) continue;

            var existing = incident.Appliances.FirstOrDefault(a => string.Equals(a.Callsign, callsign, StringComparison.OrdinalIgnoreCase));
            if (existing?.Status == ApplianceStatus.OnScene) continue;

            var there = new Coordinate((float)incident.Latitude.Value, (float)incident.Longitude.Value);
            if (Coordinate.DistanceEstimateInMeter(here, there) <= radiusMeters)
                await incidentService.SetSingleApplianceStatusAsync(organisationId, incident.Id, callsign, ApplianceStatus.OnScene, ct);
        }
    }
}
