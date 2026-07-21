using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Tests;

[Collection("Api")]
public class NavigateModeTests(CommandApiFactory factory)
{
    private readonly HttpClient _operatorA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);
    private readonly HttpClient _integrationA = factory.AsIntegration(Seed.IntegrationKeyAPlaintext);

    // Own device + own incident per test, not the shared Seed.DeviceA/
    // IncidentA -- Device.Callsign and attendance are both mutable state,
    // and the "Api" collection's database isn't reset between tests.
    private async Task<(string DeviceToken, string ExternalReference)> NewDeviceAndIncidentAsync(string externalReference)
    {
        var pairing = await _operatorA.PostAsJsonAsync("/api/devices", new { label = $"Nav test {externalReference}", orgUnitId = Seed.StationA });
        var device = (await pairing.Content.ReadFromJsonAsync<CreateDeviceResponse>(ClientExtensions.Json))!;

        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", new CreateIncidentRequest(
            externalReference, "RTC", null, "Nav test address", 50.5472, -4.1462, "KV57", DateTimeOffset.UtcNow));

        return (device.PairingToken, externalReference);
    }

    private async Task<IncidentDto> GetIncidentByReferenceAsync(string externalReference)
    {
        var all = await _operatorA.GetListAsync<IncidentSummaryDto>("/api/incidents?activeOnly=false");
        var summary = all.Single(i => i.ExternalReference == externalReference);
        return (await _operatorA.GetFromJsonAsync<IncidentDto>($"/api/incidents/{summary.Id}", ClientExtensions.Json))!;
    }

    [Fact]
    public async Task Start_navigation_with_no_callsign_set_still_succeeds_and_touches_no_attendance()
    {
        var (deviceToken, externalReference) = await NewDeviceAndIncidentAsync("NAV-NO-CALLSIGN-1");
        var incident = await GetIncidentByReferenceAsync(externalReference);

        var response = await factory.AsDevice(deviceToken).PostAsync($"/api/tablet/incidents/{incident.Id}/start-navigation", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await GetIncidentByReferenceAsync(externalReference);
        Assert.Empty(after.Appliances);
    }

    [Fact]
    public async Task Start_navigation_upserts_this_devices_own_appliance_to_EnRoute()
    {
        var (deviceToken, externalReference) = await NewDeviceAndIncidentAsync("NAV-UPSERT-1");
        var incident = await GetIncidentByReferenceAsync(externalReference);

        var deviceId = (await _operatorA.GetListAsync<DeviceDto>("/api/devices")).Single(d => d.Label == "Nav test NAV-UPSERT-1").Id;
        var setCallsign = await _operatorA.PutAsJsonAsync($"/api/devices/{deviceId}/callsign", "KV57P1");
        Assert.Equal(HttpStatusCode.OK, setCallsign.StatusCode);

        var start = await factory.AsDevice(deviceToken).PostAsync($"/api/tablet/incidents/{incident.Id}/start-navigation", null);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        var afterFirst = await GetIncidentByReferenceAsync(externalReference);
        Assert.Equal(ApplianceStatus.EnRoute, afterFirst.Appliances.Single(a => a.Callsign == "KV57P1").Status);

        // Second call is update-in-place, not a second row.
        var again = await factory.AsDevice(deviceToken).PostAsync($"/api/tablet/incidents/{incident.Id}/start-navigation", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var afterSecond = await GetIncidentByReferenceAsync(externalReference);
        Assert.Single(afterSecond.Appliances, a => a.Callsign == "KV57P1");
    }

    [Fact]
    public async Task Start_navigation_never_touches_other_appliances_attendance()
    {
        var (deviceToken, externalReference) = await NewDeviceAndIncidentAsync("NAV-OTHERS-1");
        var incident = await GetIncidentByReferenceAsync(externalReference);

        await _integrationA.PutAsJsonAsync($"/api/integrations/incidents/{externalReference}/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("KV57P2", ApplianceStatus.OnScene)]));

        var deviceId = (await _operatorA.GetListAsync<DeviceDto>("/api/devices")).Single(d => d.Label == "Nav test NAV-OTHERS-1").Id;
        await _operatorA.PutAsJsonAsync($"/api/devices/{deviceId}/callsign", "KV57P1");
        await factory.AsDevice(deviceToken).PostAsync($"/api/tablet/incidents/{incident.Id}/start-navigation", null);

        var after = await GetIncidentByReferenceAsync(externalReference);
        Assert.Equal(2, after.Appliances.Count);
        Assert.Equal(ApplianceStatus.OnScene, after.Appliances.Single(a => a.Callsign == "KV57P2").Status);
        Assert.Equal(ApplianceStatus.EnRoute, after.Appliances.Single(a => a.Callsign == "KV57P1").Status);
    }
}
