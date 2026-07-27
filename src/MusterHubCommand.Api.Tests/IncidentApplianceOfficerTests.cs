using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Tests;

[Collection("Api")]
public class IncidentApplianceOfficerTests(CommandApiFactory factory)
{
    private readonly HttpClient _integrationA = factory.AsIntegration(Seed.IntegrationKeyAPlaintext);
    private readonly HttpClient _operatorA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);

    private static CreateIncidentRequest NewIncident(string externalReference) => new(
        externalReference, "RTC", "Two vehicle RTC", "A386, Tavistock", 50.5472, -4.1462, "KV57", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Officer_in_charge_resolves_a_real_employee_or_keeps_free_text()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("OFFICER-1"));
        await _integrationA.PutAsJsonAsync("/api/integrations/incidents/OFFICER-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.OnScene), new SetApplianceEntry("P57P2", ApplianceStatus.OnScene)]));

        var incident = await GetByReferenceAsync("OFFICER-1");
        var p57p1 = incident.Appliances.Single(a => a.Callsign == "P57P1").Id;
        var p57p2 = incident.Appliances.Single(a => a.Callsign == "P57P2").Id;

        var withEmployee = await _operatorA.PatchAsJsonAsync($"/api/incidents/{incident.Id}/appliances/{p57p1}/officer",
            new SetApplianceOfficerRequest(Seed.CrewAEmployee, null));
        var afterEmployee = (await withEmployee.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        var p57p1AfterEmployee = afterEmployee.Appliances.Single(a => a.Callsign == "P57P1");
        Assert.Equal(Seed.CrewAEmployee, p57p1AfterEmployee.OfficerInChargeEmployeeId);
        Assert.Equal("Crew A", p57p1AfterEmployee.OfficerInChargeName); // resolved server-side

        var withFreeText = await _operatorA.PatchAsJsonAsync($"/api/incidents/{incident.Id}/appliances/{p57p2}/officer",
            new SetApplianceOfficerRequest(null, "Someone Not In The System"));
        var afterFreeText = (await withFreeText.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        var p57p2AfterFreeText = afterFreeText.Appliances.Single(a => a.Callsign == "P57P2");
        Assert.Null(p57p2AfterFreeText.OfficerInChargeEmployeeId);
        Assert.Equal("Someone Not In The System", p57p2AfterFreeText.OfficerInChargeName);
    }

    [Fact]
    public async Task Officer_in_charge_survives_a_vision_resync_that_changes_status()
    {
        // Same carry-forward regression as sector/resource-kind (see
        // IncidentSectorTests' own comment) -- SetAppliancesAsync deletes
        // and recreates every IncidentAppliance row on every Vision push,
        // so a Control-set officer assignment must be explicitly snapshotted
        // and re-applied by callsign, or a routine resync silently wipes it.
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("OFFICER-RESYNC-1"));
        await _integrationA.PutAsJsonAsync("/api/integrations/incidents/OFFICER-RESYNC-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.Mobilised)]));

        var incident = await GetByReferenceAsync("OFFICER-RESYNC-1");
        var p57p1Id = incident.Appliances.Single(a => a.Callsign == "P57P1").Id;
        await _operatorA.PatchAsJsonAsync($"/api/incidents/{incident.Id}/appliances/{p57p1Id}/officer",
            new SetApplianceOfficerRequest(Seed.CrewAEmployee, null));

        var resync = await _integrationA.PutAsJsonAsync("/api/integrations/incidents/OFFICER-RESYNC-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.EnRoute)]));
        var afterResync = (await resync.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        var p57p1AfterResync = afterResync.Appliances.Single(a => a.Callsign == "P57P1");
        Assert.Equal(ApplianceStatus.EnRoute, p57p1AfterResync.Status); // the actual resync did apply
        Assert.Equal(Seed.CrewAEmployee, p57p1AfterResync.OfficerInChargeEmployeeId); // but the officer survived it
        Assert.Equal("Crew A", p57p1AfterResync.OfficerInChargeName);
    }

    private async Task<IncidentDto> GetByReferenceAsync(string externalReference)
    {
        var all = await _operatorA.GetListAsync<IncidentSummaryDto>("/api/incidents?activeOnly=false");
        var summary = all.Single(i => i.ExternalReference == externalReference);
        var response = await _operatorA.GetAsync($"/api/incidents/{summary.Id}");
        return (await response.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
    }
}
