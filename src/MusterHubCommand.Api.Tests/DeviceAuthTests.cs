using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Tests;

[Collection("Api")]
public class DeviceAuthTests(CommandApiFactory factory)
{
    private readonly HttpClient _operatorA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);
    private readonly HttpClient _integrationA = factory.AsIntegration(Seed.IntegrationKeyAPlaintext);

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
        // Tagged with the device's own callsign, not left blank -- a
        // timeline entry has to say WHICH appliance left it, not just
        // "some crew, somewhere."
        Assert.Equal(Seed.DeviceACallsign, note.AuthorName);
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

    // The property this whole scoping change exists for: a station can
    // have more than one appliance out at once, and one crew has no
    // business seeing full detail of a job a different appliance from the
    // SAME station is attending, not it.
    [Fact]
    public async Task Device_cannot_see_or_read_an_incident_a_different_appliance_at_the_same_station_is_attending()
    {
        var pairing = await _operatorA.PostAsJsonAsync("/api/devices", new { label = "Second Tavistock tablet", orgUnitId = Seed.StationA });
        var otherDevice = (await pairing.Content.ReadFromJsonAsync<CreateDeviceResponse>(ClientExtensions.Json))!;
        var setCallsign = await _operatorA.PutAsJsonAsync($"/api/devices/{otherDevice.Id}/callsign", "KV57P2");
        Assert.Equal(HttpStatusCode.OK, setCallsign.StatusCode);

        // A second incident at the SAME station, attended only by KV57P2 --
        // Seed.IncidentA is already attended by Seed.DeviceA's own KV57P1.
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", new CreateIncidentRequest(
            "SNOOP-CHECK-1", "RTC", null, "Other appliance's job", 50.55, -4.15, "KV57", DateTimeOffset.UtcNow));
        await _integrationA.PutAsJsonAsync("/api/integrations/incidents/SNOOP-CHECK-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("KV57P2", ApplianceStatus.Mobilised)]));
        var otherIncidentId = (await _operatorA.GetListAsync<IncidentSummaryDto>("/api/incidents?activeOnly=false"))
            .Single(i => i.ExternalReference == "SNOOP-CHECK-1").Id;

        var deviceATablet = factory.AsDevice(Seed.DeviceATokenPlaintext);
        var otherTablet = factory.AsDevice(otherDevice.PairingToken);

        // Neither device's list includes the incident it isn't attending.
        var deviceAIncidents = await deviceATablet.GetListAsync<IncidentSummaryDto>("/api/tablet/incidents");
        Assert.DoesNotContain(deviceAIncidents, i => i.Id == otherIncidentId);
        var otherDeviceIncidents = await otherTablet.GetListAsync<IncidentSummaryDto>("/api/tablet/incidents");
        Assert.DoesNotContain(otherDeviceIncidents, i => i.Id == Seed.IncidentA);

        // Nor can either fetch the other's incident directly by id.
        var deviceAReadsOther = await deviceATablet.GetAsync($"/api/tablet/incidents/{otherIncidentId}");
        Assert.Equal(HttpStatusCode.NotFound, deviceAReadsOther.StatusCode);
        var otherReadsDeviceA = await otherTablet.GetAsync($"/api/tablet/incidents/{Seed.IncidentA}");
        Assert.Equal(HttpStatusCode.NotFound, otherReadsDeviceA.StatusCode);

        // Each still sees its own.
        Assert.Contains(deviceAIncidents, i => i.Id == Seed.IncidentA);
        Assert.Contains(otherDeviceIncidents, i => i.Id == otherIncidentId);
    }

    [Fact]
    public async Task Device_with_no_callsign_sees_nothing_and_the_device_endpoint_reports_it()
    {
        var pairing = await _operatorA.PostAsJsonAsync("/api/devices", new { label = "Unconfigured tablet", orgUnitId = Seed.StationA });
        var device = (await pairing.Content.ReadFromJsonAsync<CreateDeviceResponse>(ClientExtensions.Json))!;
        var tablet = factory.AsDevice(device.PairingToken);

        // Same station as Seed.IncidentA, which genuinely is open -- an
        // empty list here has to come from the fail-closed no-callsign
        // rule, not from there being nothing to see.
        var incidents = await tablet.GetListAsync<IncidentSummaryDto>("/api/tablet/incidents");
        Assert.Empty(incidents);

        var deviceInfo = await tablet.GetFromJsonAsync<TabletDeviceDto>("/api/tablet/device", ClientExtensions.Json);
        Assert.Null(deviceInfo!.Callsign);
    }
}
