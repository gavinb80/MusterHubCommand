using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Tests;

[Collection("Api")]
public class GeofenceTests(CommandApiFactory factory)
{
    private readonly HttpClient _operatorA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);
    private readonly HttpClient _integrationA = factory.AsIntegration(Seed.IntegrationKeyAPlaintext);

    private const double IncidentLat = 50.5000;
    private const double IncidentLng = -4.1000;
    // ~111,320m per degree of latitude -- these are approximate offsets in
    // metres north of the incident, not exact geodesics, but well clear of
    // the 50m default and any radius these tests configure.
    private const double MetresPerDegreeLat = 111320;

    private static double LatOffset(double metres) => IncidentLat + metres / MetresPerDegreeLat;

    // preAttend seeds a Mobilised attendance row for the device's own
    // callsign before returning -- geofence entry only ever transitions an
    // ALREADY-attending appliance to OnScene (see GeofenceService's own
    // comment on why), it never creates the attendance relationship from
    // proximity alone. Tests proving that boundary pass preAttend: false.
    private async Task<(string DeviceToken, Guid IncidentId)> NewDeviceAndIncidentAsync(
        string externalReference, string callsign = "KV57P1", bool preAttend = true)
    {
        var pairing = await _operatorA.PostAsJsonAsync("/api/devices", new { label = $"Geofence test {externalReference}", orgUnitId = Seed.StationA });
        var device = (await pairing.Content.ReadFromJsonAsync<CreateDeviceResponse>(ClientExtensions.Json))!;

        if (!string.IsNullOrEmpty(callsign))
        {
            var setCallsign = await _operatorA.PutAsJsonAsync($"/api/devices/{device.Id}/callsign", callsign);
            Assert.Equal(HttpStatusCode.OK, setCallsign.StatusCode);
        }

        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", new CreateIncidentRequest(
            externalReference, "RTC", null, "Geofence test address", IncidentLat, IncidentLng, "KV57", DateTimeOffset.UtcNow));

        if (preAttend && !string.IsNullOrEmpty(callsign))
        {
            await _integrationA.PutAsJsonAsync($"/api/integrations/incidents/{externalReference}/appliances",
                new SetAppliancesRequest([new SetApplianceEntry(callsign, ApplianceStatus.Mobilised)]));
        }

        var all = await _operatorA.GetListAsync<IncidentSummaryDto>("/api/incidents?activeOnly=false");
        var incidentId = all.Single(i => i.ExternalReference == externalReference).Id;

        return (device.PairingToken, incidentId);
    }

    private async Task<IncidentDto> GetIncidentAsync(Guid id) =>
        (await _operatorA.GetFromJsonAsync<IncidentDto>($"/api/incidents/{id}", ClientExtensions.Json))!;

    [Fact]
    public async Task Entering_the_default_radius_marks_an_already_attending_appliance_OnScene()
    {
        var (deviceToken, incidentId) = await NewDeviceAndIncidentAsync("GEOFENCE-DEFAULT-1");

        // 30m away -- inside the 50m default, no OrganisationSettings row
        // configured for this test.
        var response = await factory.AsDevice(deviceToken).PostAsJsonAsync("/api/tablet/location",
            new UpdateDeviceLocationRequest(LatOffset(30), IncidentLng));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var incident = await GetIncidentAsync(incidentId);
        Assert.Equal(ApplianceStatus.OnScene, incident.Appliances.Single(a => a.Callsign == "KV57P1").Status);
    }

    [Fact]
    public async Task Never_creates_a_first_ever_attendance_row_from_proximity_alone()
    {
        // Not pre-attended -- a device driving within radius of an incident
        // it was never dispatched to must not self-assign to it. This is
        // the property TabletIncidentsController's own visibility scoping
        // depends on: attendance is the ONLY thing that makes an incident
        // readable to a device, so geofence auto-creating it here would be
        // a back door around that.
        var (deviceToken, incidentId) = await NewDeviceAndIncidentAsync("GEOFENCE-NOT-ATTENDING-1", preAttend: false);

        var response = await factory.AsDevice(deviceToken).PostAsJsonAsync("/api/tablet/location",
            new UpdateDeviceLocationRequest(IncidentLat, IncidentLng));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var incident = await GetIncidentAsync(incidentId);
        Assert.Empty(incident.Appliances);
    }

    [Fact]
    public async Task Outside_the_radius_does_nothing()
    {
        var (deviceToken, incidentId) = await NewDeviceAndIncidentAsync("GEOFENCE-OUTSIDE-1");

        // 500m away -- outside the 50m default.
        var response = await factory.AsDevice(deviceToken).PostAsJsonAsync("/api/tablet/location",
            new UpdateDeviceLocationRequest(LatOffset(500), IncidentLng));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var incident = await GetIncidentAsync(incidentId);
        Assert.Equal(ApplianceStatus.Mobilised, incident.Appliances.Single(a => a.Callsign == "KV57P1").Status);
    }

    [Fact]
    public async Task No_callsign_set_is_a_no_op()
    {
        var (deviceToken, incidentId) = await NewDeviceAndIncidentAsync("GEOFENCE-NO-CALLSIGN-1", callsign: "");

        var response = await factory.AsDevice(deviceToken).PostAsJsonAsync("/api/tablet/location",
            new UpdateDeviceLocationRequest(IncidentLat, IncidentLng));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var incident = await GetIncidentAsync(incidentId);
        Assert.Empty(incident.Appliances);
    }

    [Fact]
    public async Task Already_OnScene_is_not_rewritten_on_a_later_report_still_inside_the_radius()
    {
        var (deviceToken, incidentId) = await NewDeviceAndIncidentAsync("GEOFENCE-IDEMPOTENT-1");
        var device = factory.AsDevice(deviceToken);

        await device.PostAsJsonAsync("/api/tablet/location", new UpdateDeviceLocationRequest(IncidentLat, IncidentLng));
        var afterFirst = await GetIncidentAsync(incidentId);
        var firstUpdatedAt = afterFirst.Appliances.Single(a => a.Callsign == "KV57P1").UpdatedAtUtc;

        await Task.Delay(50); // guarantee a later report has a distinguishable timestamp if it (wrongly) rewrites
        await device.PostAsJsonAsync("/api/tablet/location", new UpdateDeviceLocationRequest(LatOffset(10), IncidentLng));
        var afterSecond = await GetIncidentAsync(incidentId);
        var secondUpdatedAt = afterSecond.Appliances.Single(a => a.Callsign == "KV57P1").UpdatedAtUtc;

        Assert.Equal(firstUpdatedAt, secondUpdatedAt);
        // Same property, at the timeline level -- a second report that's
        // still just "already OnScene" must not log a duplicate
        // ResourceChange entry every time a device re-reports its position
        // from a standstill.
        Assert.Equal(afterFirst.Updates.Count, afterSecond.Updates.Count);
    }

    [Fact]
    public async Task Custom_org_radius_is_respected()
    {
        var (deviceToken, incidentId) = await NewDeviceAndIncidentAsync("GEOFENCE-CUSTOM-RADIUS-1");

        var setRadius = await _operatorA.PutAsJsonAsync("/api/organisation-settings", new UpdateOrganisationSettingsRequest(200, false));
        Assert.Equal(HttpStatusCode.OK, setRadius.StatusCode);

        // 150m away -- outside the 50m default, inside the 200m custom radius.
        var response = await factory.AsDevice(deviceToken).PostAsJsonAsync("/api/tablet/location",
            new UpdateDeviceLocationRequest(LatOffset(150), IncidentLng));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var incident = await GetIncidentAsync(incidentId);
        Assert.Equal(ApplianceStatus.OnScene, incident.Appliances.Single(a => a.Callsign == "KV57P1").Status);

        // Reset for any other test in this run relying on the 50m default --
        // OrganisationSettings is a singleton row shared across the whole
        // "Api" collection's database, same caveat as every other seeded
        // table this suite works around by using fresh rows per test.
        await _operatorA.PutAsJsonAsync("/api/organisation-settings", new UpdateOrganisationSettingsRequest(50, false));
    }
}
