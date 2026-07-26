using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Tests;

[Collection("Api")]
public class IncidentSectorTests(CommandApiFactory factory)
{
    private readonly HttpClient _integrationA = factory.AsIntegration(Seed.IntegrationKeyAPlaintext);
    private readonly HttpClient _operatorA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);

    private static CreateIncidentRequest NewIncident(string externalReference) => new(
        externalReference, "RTC", "Two vehicle RTC", "A386, Tavistock", 50.5472, -4.1462, "KV57", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Adding_a_sector_and_assigning_an_appliance_groups_it()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("SECTOR-1"));
        await _integrationA.PutAsJsonAsync("/api/integrations/incidents/SECTOR-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.OnScene)]));

        var incident = await GetByReferenceAsync("SECTOR-1");

        var addSector = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/sectors", new AddSectorRequest("Bravo"));
        Assert.Equal(HttpStatusCode.OK, addSector.StatusCode);
        var afterAddSector = (await addSector.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        var sector = Assert.Single(afterAddSector.Sectors);
        Assert.Equal("Bravo", sector.Name);

        var applianceId = afterAddSector.Appliances.Single(a => a.Callsign == "P57P1").Id;
        var assign = await _operatorA.PatchAsJsonAsync($"/api/incidents/{incident.Id}/appliances/{applianceId}/sector",
            new AssignSectorRequest(sector.Id));
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        var afterAssign = (await assign.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        Assert.Equal(sector.Id, afterAssign.Appliances.Single(a => a.Callsign == "P57P1").SectorId);
    }

    [Fact]
    public async Task Deleting_a_sector_unassigns_its_appliances_rather_than_blocking()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("SECTOR-DELETE-1"));
        await _integrationA.PutAsJsonAsync("/api/integrations/incidents/SECTOR-DELETE-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.OnScene)]));

        var incident = await GetByReferenceAsync("SECTOR-DELETE-1");
        var addSector = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/sectors", new AddSectorRequest("Bravo"));
        var afterAddSector = (await addSector.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        var sector = afterAddSector.Sectors.Single();
        var applianceId = afterAddSector.Appliances.Single().Id;
        await _operatorA.PatchAsJsonAsync($"/api/incidents/{incident.Id}/appliances/{applianceId}/sector", new AssignSectorRequest(sector.Id));

        var delete = await _operatorA.DeleteAsync($"/api/incidents/{incident.Id}/sectors/{sector.Id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        var afterDelete = (await delete.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        Assert.Empty(afterDelete.Sectors);
        Assert.Null(afterDelete.Appliances.Single().SectorId);
    }

    [Fact]
    public async Task Assigning_a_sector_from_a_different_incident_is_rejected()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("SECTOR-CROSS-1"));
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("SECTOR-CROSS-2"));
        await _integrationA.PutAsJsonAsync("/api/integrations/incidents/SECTOR-CROSS-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.OnScene)]));

        var incident1 = await GetByReferenceAsync("SECTOR-CROSS-1");
        var incident2 = await GetByReferenceAsync("SECTOR-CROSS-2");
        var addSector = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident2.Id}/sectors", new AddSectorRequest("Bravo"));
        var sector = (await addSector.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Sectors.Single();

        var applianceId = incident1.Appliances.Single().Id;
        var assign = await _operatorA.PatchAsJsonAsync($"/api/incidents/{incident1.Id}/appliances/{applianceId}/sector",
            new AssignSectorRequest(sector.Id));
        Assert.Equal(HttpStatusCode.BadRequest, assign.StatusCode);
    }

    [Fact]
    public async Task Sector_and_resource_kind_survive_a_vision_resync_that_changes_status()
    {
        // Regression test for the carry-forward fix -- SetAppliancesAsync
        // deletes and recreates every IncidentAppliance row on every Vision
        // push (see its own comment), so Control-set metadata Vision knows
        // nothing about must be explicitly snapshotted and re-applied by
        // callsign, or a routine resync silently wipes it.
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("SECTOR-RESYNC-1"));
        await _integrationA.PutAsJsonAsync("/api/integrations/incidents/SECTOR-RESYNC-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.Mobilised), new SetApplianceEntry("P57P2", ApplianceStatus.Mobilised)]));

        var incident = await GetByReferenceAsync("SECTOR-RESYNC-1");
        var addSector = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/sectors", new AddSectorRequest("Bravo"));
        var afterAddSector = (await addSector.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        var sector = afterAddSector.Sectors.Single();
        var p57p1Id = afterAddSector.Appliances.Single(a => a.Callsign == "P57P1").Id;

        await _operatorA.PatchAsJsonAsync($"/api/incidents/{incident.Id}/appliances/{p57p1Id}/sector", new AssignSectorRequest(sector.Id));
        await _operatorA.PatchAsJsonAsync($"/api/incidents/{incident.Id}/appliances/{p57p1Id}/resource-kind", new SetResourceKindRequest(ResourceKind.OfficerVehicle));

        // Vision resyncs: P57P1 moves to EnRoute, P57P3 joins, P57P2 stands
        // down by omission -- an entirely ordinary make-up push.
        var resync = await _integrationA.PutAsJsonAsync("/api/integrations/incidents/SECTOR-RESYNC-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.EnRoute), new SetApplianceEntry("P57P3", ApplianceStatus.Mobilised)]));
        var afterResync = (await resync.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        var p57p1AfterResync = afterResync.Appliances.Single(a => a.Callsign == "P57P1");
        Assert.Equal(ApplianceStatus.EnRoute, p57p1AfterResync.Status); // the actual resync did apply
        Assert.Equal(sector.Id, p57p1AfterResync.SectorId); // but the sector survived it
        Assert.Equal(ResourceKind.OfficerVehicle, p57p1AfterResync.ResourceKind); // and the resource kind too

        // The new appliance that wasn't there before has no prior metadata
        // to carry forward -- plain defaults, not an error.
        var p57p3 = afterResync.Appliances.Single(a => a.Callsign == "P57P3");
        Assert.Null(p57p3.SectorId);
        Assert.Equal(ResourceKind.Appliance, p57p3.ResourceKind);
    }

    [Fact]
    public async Task Acknowledging_an_update_is_idempotent_and_records_who()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("ACK-1"));
        var addUpdate = await _integrationA.PostAsJsonAsync("/api/integrations/incidents/ACK-1/updates",
            new AddIncidentUpdateRequest("Control Room", "Fuel spillage", IncidentUpdateType.Hazard));
        var incident = (await addUpdate.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        var update = incident.Updates.Single();

        var firstAck = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/updates/{update.Id}/acknowledge",
            new AcknowledgeUpdateRequest("Operator A"));
        var afterFirst = (await firstAck.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        var ackedUpdate = afterFirst.Updates.Single();
        Assert.NotNull(ackedUpdate.AcknowledgedAtUtc);
        Assert.Equal("Operator A", ackedUpdate.AcknowledgedByName);

        // A second acknowledgement (two devices catching up on the same
        // poll) must not stomp on who acknowledged it first.
        var secondAck = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/updates/{update.Id}/acknowledge",
            new AcknowledgeUpdateRequest("Someone Else"));
        var afterSecond = (await secondAck.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        Assert.Equal("Operator A", afterSecond.Updates.Single().AcknowledgedByName);
        Assert.Equal(ackedUpdate.AcknowledgedAtUtc, afterSecond.Updates.Single().AcknowledgedAtUtc);
    }

    private async Task<IncidentDto> GetByReferenceAsync(string externalReference)
    {
        var all = await _operatorA.GetListAsync<IncidentSummaryDto>("/api/incidents?activeOnly=false");
        var summary = all.Single(i => i.ExternalReference == externalReference);
        var response = await _operatorA.GetAsync($"/api/incidents/{summary.Id}");
        return (await response.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
    }
}
