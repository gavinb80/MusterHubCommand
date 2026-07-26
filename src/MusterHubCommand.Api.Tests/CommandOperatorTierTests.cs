using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Tests;

// Control Room / Command Support (both baseline) vs Incident Commander
// (elevated) -- see CommandOperator's own comment for the three-tier
// design; Control Room and Command Support are named distinctly (Fire
// Control desk staff vs. on-scene admin/logging support) but carry the
// same permissions today. Bootstrap already grants Commander-level access
// (BootstrapTests' founder self-grant, itself a Commander-gated
// SetOperator call, only succeeds because of that), so this file is purely
// about the tier split once an org is past bootstrap: Seed.OperatorAPerson
// is an Incident Commander, Seed.CommandSupportAPerson/ControlRoomAPerson
// are the two baseline tiers, all in the same org.
[Collection("Api")]
public class CommandOperatorTierTests(CommandApiFactory factory)
{
    private readonly HttpClient _integrationA = factory.AsIntegration(Seed.IntegrationKeyAPlaintext);
    private readonly HttpClient _commanderA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);
    private readonly HttpClient _supportA = factory.AsUser(Seed.OrgA, Seed.CommandSupportAPerson);
    private readonly HttpClient _controlRoomA = factory.AsUser(Seed.OrgA, Seed.ControlRoomAPerson);

    private static CreateIncidentRequest NewIncident(string externalReference) => new(
        externalReference, "RTC", "Two vehicle RTC", "A386, Tavistock", 50.5472, -4.1462, "KV57", DateTimeOffset.UtcNow);

    private async Task<IncidentDto> GetByReferenceAsync(string externalReference)
    {
        var all = await _commanderA.GetListAsync<IncidentSummaryDto>("/api/incidents?activeOnly=false");
        var summary = all.Single(i => i.ExternalReference == externalReference);
        var response = await _commanderA.GetAsync($"/api/incidents/{summary.Id}");
        return (await response.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
    }

    [Fact]
    public async Task Cancelling_an_incident_requires_Incident_Commander()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("TIER-CANCEL-1"));
        var incident = await GetByReferenceAsync("TIER-CANCEL-1");

        var deniedForSupport = await _supportA.DeleteAsync($"/api/incidents/{incident.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedForSupport.StatusCode);

        var allowedForCommander = await _commanderA.DeleteAsync($"/api/incidents/{incident.Id}");
        Assert.Equal(HttpStatusCode.NoContent, allowedForCommander.StatusCode);
    }

    [Fact]
    public async Task Control_Room_is_a_distinct_named_tier_but_carries_the_same_baseline_access_as_Command_Support()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("TIER-CONTROLROOM-1"));
        var incident = await GetByReferenceAsync("TIER-CONTROLROOM-1");

        var deniedCancel = await _controlRoomA.DeleteAsync($"/api/incidents/{incident.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedCancel.StatusCode);

        var allowedPatch = await _controlRoomA.PatchAsJsonAsync($"/api/incidents/{incident.Id}",
            new UpdateIncidentRequest(null, "Confirmed working fire", null, null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, allowedPatch.StatusCode);
    }

    [Fact]
    public async Task Closing_an_incident_via_the_generic_PATCH_requires_Incident_Commander()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("TIER-CLOSE-1"));
        var incident = await GetByReferenceAsync("TIER-CLOSE-1");

        var deniedForSupport = await _supportA.PatchAsJsonAsync($"/api/incidents/{incident.Id}",
            new UpdateIncidentRequest(null, null, null, null, null, null, IncidentStatus.Closed));
        Assert.Equal(HttpStatusCode.Forbidden, deniedForSupport.StatusCode);

        var allowedForCommander = await _commanderA.PatchAsJsonAsync($"/api/incidents/{incident.Id}",
            new UpdateIncidentRequest(null, null, null, null, null, null, IncidentStatus.Closed));
        Assert.Equal(HttpStatusCode.OK, allowedForCommander.StatusCode);
        var after = (await allowedForCommander.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        Assert.Equal(IncidentStatus.Closed, after.Status);
    }

    [Fact]
    public async Task Patching_a_non_status_field_on_the_same_PATCH_only_needs_Command_Support()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("TIER-PATCH-1"));
        var incident = await GetByReferenceAsync("TIER-PATCH-1");

        var patch = await _supportA.PatchAsJsonAsync($"/api/incidents/{incident.Id}",
            new UpdateIncidentRequest(null, "Confirmed working fire", null, null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var after = (await patch.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        Assert.Equal("Confirmed working fire", after.Description);
    }

    [Fact]
    public async Task Sector_CRUD_requires_Incident_Commander()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("TIER-SECTOR-1"));
        var incident = await GetByReferenceAsync("TIER-SECTOR-1");

        var addDenied = await _supportA.PostAsJsonAsync($"/api/incidents/{incident.Id}/sectors", new AddSectorRequest("Bravo"));
        Assert.Equal(HttpStatusCode.Forbidden, addDenied.StatusCode);

        var add = await _commanderA.PostAsJsonAsync($"/api/incidents/{incident.Id}/sectors", new AddSectorRequest("Bravo"));
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var sector = (await add.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Sectors.Single();

        var updateDenied = await _supportA.PatchAsJsonAsync($"/api/incidents/{incident.Id}/sectors/{sector.Id}",
            new UpdateSectorRequest("Bravo Renamed", null, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, updateDenied.StatusCode);

        var update = await _commanderA.PatchAsJsonAsync($"/api/incidents/{incident.Id}/sectors/{sector.Id}",
            new UpdateSectorRequest("Bravo Renamed", null, null, null));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var deleteDenied = await _supportA.DeleteAsync($"/api/incidents/{incident.Id}/sectors/{sector.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteDenied.StatusCode);

        var delete = await _commanderA.DeleteAsync($"/api/incidents/{incident.Id}/sectors/{sector.Id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
    }

    [Fact]
    public async Task Assigning_an_appliance_to_a_sector_is_attendance_not_hierarchy_editing_so_Command_Support_can_do_it()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident("TIER-ASSIGN-1"));
        await _integrationA.PutAsJsonAsync("/api/integrations/incidents/TIER-ASSIGN-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.OnScene)]));
        var incident = await GetByReferenceAsync("TIER-ASSIGN-1");

        var addSector = await _commanderA.PostAsJsonAsync($"/api/incidents/{incident.Id}/sectors", new AddSectorRequest("Bravo"));
        var afterAddSector = (await addSector.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        var sector = afterAddSector.Sectors.Single();
        var applianceId = afterAddSector.Appliances.Single().Id;

        var assign = await _supportA.PatchAsJsonAsync($"/api/incidents/{incident.Id}/appliances/{applianceId}/sector",
            new AssignSectorRequest(sector.Id));
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
    }

    [Fact]
    public async Task Granting_or_revoking_an_operator_tier_requires_Incident_Commander()
    {
        var employees = await _commanderA.GetListAsync<EmployeeDto>("/api/employees");
        var crewA = employees.Single(e => e.DisplayName == "Crew A");

        var deniedForSupport = await _supportA.PutAsJsonAsync($"/api/employees/{crewA.Id}/operator",
            new SetOperatorRequest(CommandOperatorTier.CommandSupport));
        Assert.Equal(HttpStatusCode.Forbidden, deniedForSupport.StatusCode);

        var grant = await _commanderA.PutAsJsonAsync($"/api/employees/{crewA.Id}/operator",
            new SetOperatorRequest(CommandOperatorTier.CommandSupport));
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);

        var afterGrant = await _commanderA.GetListAsync<EmployeeDto>("/api/employees");
        Assert.Equal(CommandOperatorTier.CommandSupport, afterGrant.Single(e => e.Id == crewA.Id).OperatorTier);

        var revoke = await _commanderA.PutAsJsonAsync($"/api/employees/{crewA.Id}/operator", new SetOperatorRequest(null));
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var afterRevoke = await _commanderA.GetListAsync<EmployeeDto>("/api/employees");
        Assert.Null(afterRevoke.Single(e => e.Id == crewA.Id).OperatorTier);
    }
}
