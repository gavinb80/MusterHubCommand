using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Tests;

[Collection("Api")]
public class IncidentLifecycleTests(CommandApiFactory factory)
{
    private readonly HttpClient _integrationA = factory.AsIntegration(Seed.IntegrationKeyAPlaintext);
    private readonly HttpClient _operatorA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);

    private static CreateIncidentRequest NewIncident(string externalReference) => new(
        externalReference, "RTC", "Two vehicle RTC", "A386, Tavistock", 50.5472, -4.1462, "KV57", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Create_is_idempotent_by_external_reference_not_a_duplicate_insert()
    {
        var request = NewIncident("IDEMPOTENT-1");

        var first = await _integrationA.PostAsJsonAsync("/api/integrations/incidents", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstDto = (await first.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        var secondRequest = request with { Description = "Updated on re-push" };
        var second = await _integrationA.PostAsJsonAsync("/api/integrations/incidents", secondRequest);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondDto = (await second.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        Assert.Equal(firstDto.Id, secondDto.Id); // same row, not a duplicate
        Assert.Equal("Updated on re-push", secondDto.Description);

        var all = await _operatorA.GetListAsync<IncidentSummaryDto>("/api/incidents?activeOnly=false");
        Assert.Single(all, i => i.ExternalReference == "IDEMPOTENT-1");
    }

    [Fact]
    public async Task Create_with_unknown_station_code_is_a_clean_400_not_a_500()
    {
        var response = await _integrationA.PostAsJsonAsync("/api/integrations/incidents",
            NewIncident("BAD-STATION-1") with { StationCode = "DOES-NOT-EXIST" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Created_incident_reports_a_real_station_name_not_blank()
    {
        // Regression test for the anonymous-caller tenant-filter bug: the
        // OrgUnit was loaded via db.Entry().Reference().LoadAsync(), which
        // applies the ambient tenant filter -- Guid.Empty for this
        // AllowAnonymous caller -- so every Vision-pushed incident came
        // back with orgUnitName == "".
        var response = await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("STATION-NAME-1"));
        var dto = (await response.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        Assert.Equal("Tavistock", dto.OrgUnitName);
    }

    [Fact]
    public async Task Patch_applies_only_the_fields_present_and_reports_the_new_station_name()
    {
        var created = await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("PATCH-1"));
        var before = (await created.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        var patch = await _integrationA.PatchAsJsonAsync("/api/integrations/incidents/PATCH-1",
            new UpdateIncidentRequest(null, "Confirmed working fire", null, null, null, "KV42", IncidentStatus.Closed));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var after = (await patch.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        Assert.Equal(before.IncidentType, after.IncidentType); // untouched field survives
        Assert.Equal("Confirmed working fire", after.Description);
        Assert.Equal(IncidentStatus.Closed, after.Status);
        Assert.NotNull(after.ClosedAtUtc);
        Assert.Equal("Plympton", after.OrgUnitName); // moved to KV42
    }

    [Fact]
    public async Task Delete_is_a_soft_cancel_the_row_still_reads_back()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("CANCEL-1"));
        var delete = await _integrationA.DeleteAsync("/api/integrations/incidents/CANCEL-1");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var read = await _operatorA.GetListAsync<IncidentSummaryDto>("/api/incidents?activeOnly=false");
        var cancelled = read.Single(i => i.ExternalReference == "CANCEL-1");
        Assert.Equal(IncidentStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task Setting_appliances_actually_inserts_rows_not_a_no_op_update()
    {
        // Regression test for the EF Added-vs-Modified bug: assigning a
        // list of brand-new IncidentAppliance objects to the navigation
        // property without also calling db.IncidentAppliances.AddRange()
        // let EF's implicit graph-fixup read the client-generated,
        // already-non-default Id as Modified rather than Added, emitting
        // UPDATEs against rows that didn't exist and throwing
        // DbUpdateConcurrencyException on every push.
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("APPLIANCES-1"));

        var set = await _integrationA.PutAsJsonAsync("/api/integrations/incidents/APPLIANCES-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.OnScene), new SetApplianceEntry("P57P2", ApplianceStatus.EnRoute)]));
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var afterFirstSet = (await set.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        Assert.Equal(2, afterFirstSet.Appliances.Count);

        // A make-up/stand-down: replace-whole, not additive -- three
        // appliances in, two out, one held over.
        var makeUp = await _integrationA.PutAsJsonAsync("/api/integrations/incidents/APPLIANCES-1/appliances",
            new SetAppliancesRequest([
                new SetApplianceEntry("P57P1", ApplianceStatus.StoodDown),
                new SetApplianceEntry("P57P2", ApplianceStatus.OnScene),
                new SetApplianceEntry("P58P1", ApplianceStatus.Mobilised),
            ]));
        var afterMakeUp = (await makeUp.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        Assert.Equal(3, afterMakeUp.Appliances.Count);
        Assert.Equal(ApplianceStatus.StoodDown, afterMakeUp.Appliances.Single(a => a.Callsign == "P57P1").Status);
    }

    [Fact]
    public async Task Dispatching_appliances_for_the_first_time_logs_one_resource_change_update_each()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("TIMELINE-DISPATCH-1"));

        var set = await _integrationA.PutAsJsonAsync("/api/integrations/incidents/TIMELINE-DISPATCH-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.Mobilised), new SetApplianceEntry("P57P2", ApplianceStatus.EnRoute)]));
        var dto = (await set.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        Assert.Equal(2, dto.Updates.Count(u => u.UpdateType == IncidentUpdateType.ResourceChange));
        Assert.Contains(dto.Updates, u => u.Text == "P57P1 Mobilised" && u.Source == IncidentUpdateSource.ControlRoom);
        Assert.Contains(dto.Updates, u => u.Text == "P57P2 EnRoute" && u.Source == IncidentUpdateSource.ControlRoom);
    }

    [Fact]
    public async Task Repeat_push_only_logs_updates_for_appliances_that_actually_changed()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("TIMELINE-REPUSH-1"));
        var first = await _integrationA.PutAsJsonAsync("/api/integrations/incidents/TIMELINE-REPUSH-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.Mobilised), new SetApplianceEntry("P57P2", ApplianceStatus.Mobilised)]));
        var afterFirst = (await first.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        var countAfterFirst = afterFirst.Updates.Count;

        // A resync of the exact same attendance, except P57P1 has moved on
        // to EnRoute -- only that one appliance genuinely changed.
        var second = await _integrationA.PutAsJsonAsync("/api/integrations/incidents/TIMELINE-REPUSH-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.EnRoute), new SetApplianceEntry("P57P2", ApplianceStatus.Mobilised)]));
        var afterSecond = (await second.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        Assert.Equal(countAfterFirst + 1, afterSecond.Updates.Count);
        Assert.Contains(afterSecond.Updates, u => u.Text == "P57P1 EnRoute");
    }

    [Fact]
    public async Task Removing_an_appliance_from_a_repush_logs_a_removal_update()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("TIMELINE-REMOVE-1"));
        await _integrationA.PutAsJsonAsync("/api/integrations/incidents/TIMELINE-REMOVE-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.Mobilised), new SetApplianceEntry("P57P2", ApplianceStatus.Mobilised)]));

        // Stand-down via omission -- P57P2 simply isn't in the next push.
        var second = await _integrationA.PutAsJsonAsync("/api/integrations/incidents/TIMELINE-REMOVE-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.Mobilised)]));
        var dto = (await second.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        Assert.Contains(dto.Updates, u => u.Text == "P57P2 removed from attendance");
    }

    [Fact]
    public async Task Updates_append_hazard_entries_and_never_remove_earlier_ones()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("TIMELINE-1"));

        await _integrationA.PostAsJsonAsync("/api/integrations/incidents/TIMELINE-1/updates",
            new AddIncidentUpdateRequest("Control Room", "First update", IncidentUpdateType.General));
        var second = await _integrationA.PostAsJsonAsync("/api/integrations/incidents/TIMELINE-1/updates",
            new AddIncidentUpdateRequest("Control Room", "Fuel spillage", IncidentUpdateType.Hazard));

        var dto = (await second.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        Assert.Equal(2, dto.Updates.Count);
        Assert.Contains(dto.Updates, u => u.Text == "First update" && u.Source == IncidentUpdateSource.ControlRoom);
        Assert.Contains(dto.Updates, u => u.Text == "Fuel spillage" && u.UpdateType == IncidentUpdateType.Hazard);
    }

    [Fact]
    public async Task Wrong_integration_key_is_401()
    {
        var response = await factory.AsIntegration("not-a-real-key")
            .PostAsJsonAsync("/api/integrations/incidents", NewIncident("WRONG-KEY-1"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_operator_cannot_create_an_incident_via_the_web_console()
    {
        var crew = factory.AsUser(Seed.OrgA, Seed.CrewAPerson);
        var response = await crew.PostAsJsonAsync("/api/incidents", NewIncident("CREW-DENIED-1"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
