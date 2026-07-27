using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Tests;

[Collection("Api")]
public class IncidentObjectiveTests(CommandApiFactory factory)
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
    public async Task Raising_an_objective_resolves_the_raiser_server_side_not_from_the_request_body()
    {
        var incident = await CreateIncidentAsync("OBJECTIVE-RAISE-1");

        var addObjective = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/objectives",
            new AddObjectiveRequest("Establish a water supply"));
        Assert.Equal(HttpStatusCode.OK, addObjective.StatusCode);
        var afterAdd = (await addObjective.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        var objective = Assert.Single(afterAdd.Objectives);
        Assert.Equal("Establish a water supply", objective.Text);
        Assert.Equal(IncidentObjectiveStatus.Open, objective.Status);
        // Resolved from the caller's own session, not a field on
        // AddObjectiveRequest -- there isn't one to have supplied.
        Assert.Equal("Operator A", objective.RaisedByName);
        Assert.Equal(Seed.OperatorAEmployee, objective.RaisedByEmployeeId);
    }

    [Fact]
    public async Task Raising_achieving_and_reopening_an_objective_never_writes_a_timeline_entry()
    {
        var incident = await CreateIncidentAsync("OBJECTIVE-TIMELINE-1");

        var addObjective = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/objectives",
            new AddObjectiveRequest("Make the building safe"));
        var afterAdd = (await addObjective.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        Assert.DoesNotContain(afterAdd.Updates, u => u.UpdateType == IncidentUpdateType.ActionChange);
        var objective = afterAdd.Objectives.Single();

        var achieve = await _operatorA.PostAsync($"/api/incidents/{incident.Id}/objectives/{objective.Id}/achieve", null);
        var afterAchieve = (await achieve.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        Assert.DoesNotContain(afterAchieve.Updates, u => u.UpdateType == IncidentUpdateType.ActionChange);

        var reopen = await _operatorA.PostAsync($"/api/incidents/{incident.Id}/objectives/{objective.Id}/reopen", null);
        var afterReopen = (await reopen.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        Assert.DoesNotContain(afterReopen.Updates, u => u.UpdateType == IncidentUpdateType.ActionChange);
        // Nothing else in the Timeline either -- distinct from it entirely,
        // not just missing an ActionChange-typed entry.
        Assert.Equal(afterAdd.Updates.Count, afterReopen.Updates.Count);
    }

    [Fact]
    public async Task Achieving_an_objective_is_idempotent()
    {
        var incident = await CreateIncidentAsync("OBJECTIVE-ACHIEVE-1");
        var addObjective = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/objectives",
            new AddObjectiveRequest("Search the ground floor"));
        var objective = (await addObjective.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Objectives.Single();

        var firstAchieve = await _operatorA.PostAsync($"/api/incidents/{incident.Id}/objectives/{objective.Id}/achieve", null);
        var afterFirst = (await firstAchieve.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Objectives.Single();
        Assert.Equal(IncidentObjectiveStatus.Achieved, afterFirst.Status);
        Assert.NotNull(afterFirst.AchievedAtUtc);
        Assert.Equal("Operator A", afterFirst.AchievedByName);

        var secondAchieve = await _operatorA.PostAsync($"/api/incidents/{incident.Id}/objectives/{objective.Id}/achieve", null);
        var afterSecond = (await secondAchieve.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Objectives.Single();
        Assert.Equal(afterFirst.AchievedAtUtc, afterSecond.AchievedAtUtc); // second call didn't stomp on the first
    }

    [Fact]
    public async Task Reopening_an_achieved_objective_clears_the_stamp_and_a_later_achieve_is_fresh()
    {
        var incident = await CreateIncidentAsync("OBJECTIVE-REOPEN-1");
        var addObjective = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/objectives",
            new AddObjectiveRequest("Evacuate the neighbouring properties"));
        var objective = (await addObjective.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Objectives.Single();

        await _operatorA.PostAsync($"/api/incidents/{incident.Id}/objectives/{objective.Id}/achieve", null);

        var firstReopen = await _operatorA.PostAsync($"/api/incidents/{incident.Id}/objectives/{objective.Id}/reopen", null);
        var afterFirstReopen = (await firstReopen.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Objectives.Single();
        Assert.Equal(IncidentObjectiveStatus.Open, afterFirstReopen.Status);
        Assert.Null(afterFirstReopen.AchievedAtUtc);
        Assert.Null(afterFirstReopen.AchievedByName);

        // Idempotent the same direction as achieve.
        var secondReopen = await _operatorA.PostAsync($"/api/incidents/{incident.Id}/objectives/{objective.Id}/reopen", null);
        var afterSecondReopen = (await secondReopen.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Objectives.Single();
        Assert.Equal(IncidentObjectiveStatus.Open, afterSecondReopen.Status);

        // Achieving again after a reopen is a fresh achievement, not a
        // continuation of the cleared one.
        var reAchieve = await _operatorA.PostAsync($"/api/incidents/{incident.Id}/objectives/{objective.Id}/achieve", null);
        var afterReAchieve = (await reAchieve.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Objectives.Single();
        Assert.Equal(IncidentObjectiveStatus.Achieved, afterReAchieve.Status);
        Assert.NotNull(afterReAchieve.AchievedAtUtc);
    }

    [Fact]
    public async Task Non_operator_cannot_raise_an_objective()
    {
        var incident = await CreateIncidentAsync("OBJECTIVE-DENY-1");
        var crew = factory.AsUser(Seed.OrgA, Seed.CrewAPerson);

        var response = await crew.PostAsJsonAsync($"/api/incidents/{incident.Id}/objectives", new AddObjectiveRequest("Not allowed"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tablet_can_raise_and_achieve_an_objective_only_while_attending()
    {
        // Seed's own comment: DeviceA is attending IncidentA via its callsign.
        var tabletOnScene = factory.AsDevice(Seed.DeviceATokenPlaintext);
        var addObjective = await tabletOnScene.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/objectives",
            new AddObjectiveRequest("Confirm all persons accounted for"));
        addObjective.EnsureSuccessStatusCode();
        var objective = (await addObjective.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Objectives
            .Single(o => o.Text == "Confirm all persons accounted for");
        Assert.Equal(Seed.DeviceACallsign, objective.RaisedByName);

        var achieve = await tabletOnScene.PostAsync($"/api/tablet/incidents/{Seed.IncidentA}/objectives/{objective.Id}/achieve", null);
        achieve.EnsureSuccessStatusCode();
        var achieved = (await achieve.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Objectives.Single(o => o.Id == objective.Id);
        Assert.Equal(Seed.DeviceACallsign, achieved.AchievedByName);

        // DeviceB is a different org's tablet and isn't attending IncidentA either way.
        var otherDevice = factory.AsDevice(Seed.DeviceBTokenPlaintext);
        var deniedAdd = await otherDevice.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/objectives", new AddObjectiveRequest("Nope"));
        Assert.Equal(HttpStatusCode.NotFound, deniedAdd.StatusCode);
        var deniedAchieve = await otherDevice.PostAsync($"/api/tablet/incidents/{Seed.IncidentA}/objectives/{objective.Id}/achieve", null);
        Assert.Equal(HttpStatusCode.NotFound, deniedAchieve.StatusCode);
    }

    [Fact]
    public async Task Objectives_are_ordered_by_creation_time()
    {
        var incident = await CreateIncidentAsync("OBJECTIVE-ORDER-1");
        await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/objectives", new AddObjectiveRequest("First"));
        var afterSecond = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/objectives", new AddObjectiveRequest("Second"));
        var objectives = (await afterSecond.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Objectives;

        Assert.Equal(["First", "Second"], objectives.Select(o => o.Text));
    }
}
