using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Tests;

[Collection("Api")]
public class DeviceAuthTests(CommandApiFactory factory)
{
    private readonly HttpClient _operatorA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);

    [Fact]
    public async Task Invalid_device_token_is_401()
    {
        var response = await factory.AsDevice("not-a-real-token").GetAsync("/api/tablet/incidents");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Device_only_sees_active_incidents_at_its_own_station()
    {
        // StationA2 (Plympton) has no incidents seeded -- pair a fresh
        // device there and confirm it sees nothing, while OrgA's seeded
        // device (at Tavistock) sees the seeded incident.
        var pairing = await _operatorA.PostAsJsonAsync("/api/devices", new { label = "Plympton tablet", orgUnitId = Seed.StationA2 });
        var device = (await pairing.Content.ReadFromJsonAsync<CreateDeviceResponse>(ClientExtensions.Json))!;

        var plymptonTablet = factory.AsDevice(device.PairingToken);
        var plymptonIncidents = await plymptonTablet.GetListAsync<IncidentSummaryDto>("/api/tablet/incidents");
        Assert.Empty(plymptonIncidents);

        var tavistockTablet = factory.AsDevice(Seed.DeviceATokenPlaintext);
        var tavistockIncidents = await tavistockTablet.GetListAsync<IncidentSummaryDto>("/api/tablet/incidents");
        Assert.Contains(tavistockIncidents, i => i.Id == Seed.IncidentA);
    }

    [Fact]
    public async Task Revoked_device_is_locked_out_immediately()
    {
        var pairing = await _operatorA.PostAsJsonAsync("/api/devices", new { label = "To be revoked", orgUnitId = Seed.StationA });
        var device = (await pairing.Content.ReadFromJsonAsync<CreateDeviceResponse>(ClientExtensions.Json))!;
        var tablet = factory.AsDevice(device.PairingToken);

        var beforeRevoke = await tablet.GetAsync("/api/tablet/incidents");
        Assert.Equal(HttpStatusCode.OK, beforeRevoke.StatusCode);

        var revoke = await _operatorA.DeleteAsync($"/api/devices/{device.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var afterRevoke = await tablet.GetAsync("/api/tablet/incidents");
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }

    [Fact]
    public async Task Crew_note_is_recorded_with_Crew_source_not_ControlRoom()
    {
        var tablet = factory.AsDevice(Seed.DeviceATokenPlaintext);
        var response = await tablet.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/notes",
            new AddCrewNoteRequest("Casualty extricated", null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = (await response.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        var note = dto.Updates.Single(u => u.Text == "Casualty extricated");
        Assert.Equal(IncidentUpdateSource.Crew, note.Source);
        Assert.Equal(IncidentUpdateType.Note, note.UpdateType);
    }

    [Fact]
    public async Task Device_cannot_read_an_incident_at_a_different_station_even_within_the_same_org()
    {
        // StationA2 has no incidents, but this asserts the *scoping*
        // itself, not just emptiness: a device at StationA2 must not be
        // able to fetch IncidentA (which belongs to StationA) by id, even
        // though both are in OrgA.
        var pairing = await _operatorA.PostAsJsonAsync("/api/devices", new { label = "Plympton tablet 2", orgUnitId = Seed.StationA2 });
        var device = (await pairing.Content.ReadFromJsonAsync<CreateDeviceResponse>(ClientExtensions.Json))!;

        var response = await factory.AsDevice(device.PairingToken).GetAsync($"/api/tablet/incidents/{Seed.IncidentA}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
