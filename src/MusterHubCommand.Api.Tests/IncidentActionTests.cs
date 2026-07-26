using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Tests;

[Collection("Api")]
public class IncidentActionTests(CommandApiFactory factory)
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
    public async Task Raising_a_task_assigns_an_employee_and_logs_a_timeline_entry()
    {
        var incident = await CreateIncidentAsync("ACTION-TASK-1");

        var addTask = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/actions",
            new AddActionRequest(IncidentActionKind.Task, "Secure perimeter", "Operator A", Seed.CrewAEmployee, null, null));
        Assert.Equal(HttpStatusCode.OK, addTask.StatusCode);
        var afterAdd = (await addTask.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        var action = Assert.Single(afterAdd.Actions);
        Assert.Equal(IncidentActionKind.Task, action.Kind);
        Assert.Equal(IncidentActionStatus.Open, action.Status);
        Assert.Equal(Seed.CrewAEmployee, action.AssignedToEmployeeId);
        Assert.Equal("Crew A", action.AssignedToName); // resolved server-side

        var timelineEntry = afterAdd.Updates.Single(u => u.UpdateType == IncidentUpdateType.ActionChange);
        Assert.Contains("Secure perimeter", timelineEntry.Text);
    }

    [Fact]
    public async Task Raising_a_resource_request_keeps_free_text_assignee()
    {
        var incident = await CreateIncidentAsync("ACTION-REQUEST-1");

        var addRequest = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/actions",
            new AddActionRequest(IncidentActionKind.ResourceRequest, "Need a second pump", "Operator A", null, "Station Officer", null));
        var afterAdd = (await addRequest.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        var action = afterAdd.Actions.Single();
        Assert.Equal(IncidentActionKind.ResourceRequest, action.Kind);
        Assert.Null(action.AssignedToEmployeeId);
        Assert.Equal("Station Officer", action.AssignedToName);
    }

    [Fact]
    public async Task Acknowledging_an_action_is_idempotent_and_moves_it_to_acknowledged()
    {
        var incident = await CreateIncidentAsync("ACTION-ACK-1");
        var addTask = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/actions",
            new AddActionRequest(IncidentActionKind.Task, "Check gas main", null));
        var action = (await addTask.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Actions.Single();

        var firstAck = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/actions/{action.Id}/acknowledge",
            new AcknowledgeActionRequest("Operator A"));
        var afterFirst = (await firstAck.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Actions.Single();
        Assert.Equal(IncidentActionStatus.Acknowledged, afterFirst.Status);
        Assert.NotNull(afterFirst.AcknowledgedAtUtc);

        var secondAck = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/actions/{action.Id}/acknowledge",
            new AcknowledgeActionRequest("Someone Else"));
        var afterSecond = (await secondAck.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Actions.Single();
        Assert.Equal("Operator A", afterSecond.AcknowledgedByName); // second call didn't stomp on the first
        Assert.Equal(afterFirst.AcknowledgedAtUtc, afterSecond.AcknowledgedAtUtc);
    }

    [Fact]
    public async Task Resolving_an_action_is_idempotent_and_rejects_a_non_terminal_status()
    {
        var incident = await CreateIncidentAsync("ACTION-RESOLVE-1");
        var addTask = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/actions",
            new AddActionRequest(IncidentActionKind.Task, "Cordon the road", null));
        var action = (await addTask.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Actions.Single();

        var badResolve = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/actions/{action.Id}/resolve",
            new ResolveActionRequest(IncidentActionStatus.Open));
        Assert.Equal(HttpStatusCode.BadRequest, badResolve.StatusCode);

        var firstResolve = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/actions/{action.Id}/resolve",
            new ResolveActionRequest(IncidentActionStatus.Completed, "Operator A"));
        var afterFirst = (await firstResolve.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Actions.Single();
        Assert.Equal(IncidentActionStatus.Completed, afterFirst.Status);

        var secondResolve = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/actions/{action.Id}/resolve",
            new ResolveActionRequest(IncidentActionStatus.Declined, "Someone Else"));
        var afterSecond = (await secondResolve.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Actions.Single();
        Assert.Equal(IncidentActionStatus.Completed, afterSecond.Status); // second call didn't overwrite the first resolution
        Assert.Equal("Operator A", afterSecond.ResolvedByName);
    }

    [Fact]
    public async Task Raising_an_action_against_a_sector_from_a_different_incident_is_rejected()
    {
        var incident1 = await CreateIncidentAsync("ACTION-CROSS-1");
        var incident2 = await CreateIncidentAsync("ACTION-CROSS-2");
        var addSector = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident2.Id}/sectors", new AddSectorRequest("Bravo"));
        var sector = (await addSector.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Sectors.Single();

        var response = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident1.Id}/actions",
            new AddActionRequest(IncidentActionKind.Task, "Wrong incident's sector", null, null, null, sector.Id));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Replying_to_an_update_links_to_the_right_parent()
    {
        var incident = await CreateIncidentAsync("REPLY-1");
        var original = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/updates",
            new AddIncidentUpdateRequest("Operator A", "Do you need a second pump?"));
        var originalUpdate = (await original.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Updates.Single();

        var reply = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/updates",
            new AddIncidentUpdateRequest("Operator A", "Yes please", ReplyToUpdateId: originalUpdate.Id));
        var afterReply = (await reply.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        var replyUpdate = afterReply.Updates.Single(u => u.Text == "Yes please");
        Assert.Equal(originalUpdate.Id, replyUpdate.ReplyToUpdateId);
    }

    [Fact]
    public async Task Replying_to_an_update_from_a_different_incident_is_rejected()
    {
        var incident1 = await CreateIncidentAsync("REPLY-CROSS-1");
        var incident2 = await CreateIncidentAsync("REPLY-CROSS-2");
        var original = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident2.Id}/updates",
            new AddIncidentUpdateRequest("Operator A", "Message on incident 2"));
        var originalUpdate = (await original.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Updates.Single();

        var response = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident1.Id}/updates",
            new AddIncidentUpdateRequest("Operator A", "Wrong incident's message", ReplyToUpdateId: originalUpdate.Id));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
