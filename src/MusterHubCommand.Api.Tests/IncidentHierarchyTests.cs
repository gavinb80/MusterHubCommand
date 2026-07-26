using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Tests;

[Collection("Api")]
public class IncidentHierarchyTests(CommandApiFactory factory)
{
    private readonly HttpClient _integrationA = factory.AsIntegration(Seed.IntegrationKeyAPlaintext);
    private readonly HttpClient _operatorA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);

    private static CreateIncidentRequest NewIncident(string externalReference) => new(
        externalReference, "RTC", "Two vehicle RTC", "A386, Tavistock", 50.5472, -4.1462, "KV57", DateTimeOffset.UtcNow);

    private async Task<IncidentDto> CreateIncidentAsync(string externalReference)
    {
        var response = await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident(externalReference));
        return (await response.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
    }

    [Fact]
    public async Task Adding_a_child_node_nests_it_under_its_parent()
    {
        var incident = await CreateIncidentAsync("HIER-CHILD-1");

        var addRoot = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/sectors", new AddSectorRequest("Sector 1"));
        var root = (await addRoot.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Sectors.Single();

        var addChild = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/sectors",
            new AddSectorRequest("Sector 1 Commander", root.Id));
        Assert.Equal(HttpStatusCode.OK, addChild.StatusCode);
        var afterAddChild = (await addChild.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        var child = afterAddChild.Sectors.Single(s => s.Name == "Sector 1 Commander");
        Assert.Equal(root.Id, child.ParentId);
    }

    [Fact]
    public async Task Reparenting_a_node_moves_it_without_disturbing_its_own_children()
    {
        var incident = await CreateIncidentAsync("HIER-REPARENT-1");

        var a = await AddNodeAsync(incident.Id, "A", null);
        var b = await AddNodeAsync(incident.Id, "B", a.Id);
        var c = await AddNodeAsync(incident.Id, "C", b.Id); // A -> B -> C
        var d = await AddNodeAsync(incident.Id, "D", null); // a second root

        var move = await _operatorA.PatchAsJsonAsync($"/api/incidents/{incident.Id}/sectors/{b.Id}",
            new UpdateSectorRequest("B", d.Id, null, null)); // move B (and C under it) from A to D
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        var afterMove = (await move.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        Assert.Equal(d.Id, afterMove.Sectors.Single(s => s.Id == b.Id).ParentId);
        Assert.Equal(b.Id, afterMove.Sectors.Single(s => s.Id == c.Id).ParentId); // C is untouched, still under B
    }

    [Fact]
    public async Task Reparenting_a_node_under_itself_is_rejected()
    {
        var incident = await CreateIncidentAsync("HIER-SELF-1");
        var a = await AddNodeAsync(incident.Id, "A", null);

        var move = await _operatorA.PatchAsJsonAsync($"/api/incidents/{incident.Id}/sectors/{a.Id}",
            new UpdateSectorRequest("A", a.Id, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, move.StatusCode);
    }

    [Fact]
    public async Task Reparenting_a_node_under_its_own_descendant_is_rejected()
    {
        var incident = await CreateIncidentAsync("HIER-CYCLE-1");
        var a = await AddNodeAsync(incident.Id, "A", null);
        var b = await AddNodeAsync(incident.Id, "B", a.Id);
        var c = await AddNodeAsync(incident.Id, "C", b.Id); // A -> B -> C

        // Moving A under C would mean C's own ancestor chain has to pass
        // through A again -- a cycle nothing above it could ever resolve.
        var move = await _operatorA.PatchAsJsonAsync($"/api/incidents/{incident.Id}/sectors/{a.Id}",
            new UpdateSectorRequest("A", c.Id, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, move.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_node_removes_its_whole_subtree_but_only_unassigns_appliances()
    {
        var incident = await CreateIncidentAsync("HIER-DELETE-1");
        await _integrationA.PutAsJsonAsync($"/api/integrations/incidents/HIER-DELETE-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", ApplianceStatus.OnScene)]));

        var a = await AddNodeAsync(incident.Id, "A", null);
        var b = await AddNodeAsync(incident.Id, "B", a.Id); // A -> B

        var latest = await GetAsync(incident.Id);
        var appliance = latest.Appliances.Single();
        await _operatorA.PatchAsJsonAsync($"/api/incidents/{incident.Id}/appliances/{appliance.Id}/sector",
            new AssignSectorRequest(b.Id));

        var delete = await _operatorA.DeleteAsync($"/api/incidents/{incident.Id}/sectors/{a.Id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        var afterDelete = (await delete.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        Assert.Empty(afterDelete.Sectors); // both A and B gone
        var applianceAfter = afterDelete.Appliances.Single();
        Assert.Null(applianceAfter.SectorId); // unassigned, not deleted
        Assert.Equal("P57P1", applianceAfter.Callsign);
    }

    [Fact]
    public async Task Person_in_charge_resolves_a_real_employee_or_keeps_free_text()
    {
        var incident = await CreateIncidentAsync("HIER-PERSON-1");

        var withEmployee = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/sectors",
            new AddSectorRequest("Sector 1", null, Seed.CrewAEmployee, null));
        var afterEmployee = (await withEmployee.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        var employeeNode = afterEmployee.Sectors.Single(s => s.Name == "Sector 1");
        Assert.Equal(Seed.CrewAEmployee, employeeNode.PersonInChargeEmployeeId);
        Assert.Equal("Crew A", employeeNode.PersonInChargeName); // resolved server-side

        var withFreeText = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/sectors",
            new AddSectorRequest("Sector 2", null, null, "Someone Not In The System"));
        var afterFreeText = (await withFreeText.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        var freeTextNode = afterFreeText.Sectors.Single(s => s.Name == "Sector 2");
        Assert.Null(freeTextNode.PersonInChargeEmployeeId);
        Assert.Equal("Someone Not In The System", freeTextNode.PersonInChargeName);
    }

    [Fact]
    public async Task Adding_a_node_with_a_parent_from_a_different_incident_is_rejected()
    {
        var incident1 = await CreateIncidentAsync("HIER-CROSS-1");
        var incident2 = await CreateIncidentAsync("HIER-CROSS-2");
        var parentInIncident2 = await AddNodeAsync(incident2.Id, "Sector in incident 2", null);

        var response = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident1.Id}/sectors",
            new AddSectorRequest("Trying to nest across incidents", parentInIncident2.Id));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<IncidentSectorDto> AddNodeAsync(Guid incidentId, string name, Guid? parentId)
    {
        var response = await _operatorA.PostAsJsonAsync($"/api/incidents/{incidentId}/sectors", new AddSectorRequest(name, parentId));
        var incident = (await response.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        return incident.Sectors.Single(s => s.Name == name);
    }

    private async Task<IncidentDto> GetAsync(Guid incidentId)
    {
        var response = await _operatorA.GetAsync($"/api/incidents/{incidentId}");
        return (await response.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
    }
}
