using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Tests;

[Collection("Api")]
public class IncidentRiskTests(CommandApiFactory factory)
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
    public async Task Raising_a_risk_resolves_the_raiser_server_side_not_from_the_request_body()
    {
        var incident = await CreateIncidentAsync("RISK-RAISE-1");

        var addRisk = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/risks",
            new AddRiskRequest("Unstable roof structure", IncidentRiskLevel.High, "Establish a 10m exclusion zone"));
        Assert.Equal(HttpStatusCode.OK, addRisk.StatusCode);
        var afterAdd = (await addRisk.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;

        var risk = Assert.Single(afterAdd.Risks);
        Assert.Equal("Unstable roof structure", risk.Description);
        Assert.Equal(IncidentRiskLevel.High, risk.RiskLevel);
        Assert.Equal("Establish a 10m exclusion zone", risk.ControlMeasure);
        Assert.Equal(IncidentRiskStatus.Identified, risk.Status);
        // Resolved from the caller's own session, not a field on
        // AddRiskRequest -- there isn't one to have supplied.
        Assert.Equal("Operator A", risk.RaisedByName);
        Assert.Equal(Seed.OperatorAEmployee, risk.RaisedByEmployeeId);
    }

    [Fact]
    public async Task Raising_a_risk_writes_a_timeline_entry_but_controlling_and_reopening_it_do_not()
    {
        var incident = await CreateIncidentAsync("RISK-TIMELINE-1");

        var addRisk = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/risks",
            new AddRiskRequest("Live electrical supply nearby", IncidentRiskLevel.Medium, null));
        var afterAdd = (await addRisk.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        // The inverse of Objectives' own rule -- a newly-identified hazard
        // IS a real event worth broadcasting.
        Assert.Contains(afterAdd.Updates, u => u.UpdateType == IncidentUpdateType.Hazard);
        var risk = afterAdd.Risks.Single();
        var updateCountAfterRaise = afterAdd.Updates.Count;

        var control = await _operatorA.PostAsync($"/api/incidents/{incident.Id}/risks/{risk.Id}/control", null);
        var afterControl = (await control.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        Assert.Equal(updateCountAfterRaise, afterControl.Updates.Count);

        var reopen = await _operatorA.PostAsync($"/api/incidents/{incident.Id}/risks/{risk.Id}/reopen", null);
        var afterReopen = (await reopen.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
        Assert.Equal(updateCountAfterRaise, afterReopen.Updates.Count);
    }

    [Fact]
    public async Task Controlling_a_risk_is_idempotent()
    {
        var incident = await CreateIncidentAsync("RISK-CONTROL-1");
        var addRisk = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/risks",
            new AddRiskRequest("Fuel spill on carriageway", IncidentRiskLevel.Medium, "Foam blanket applied"));
        var risk = (await addRisk.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Risks.Single();

        var firstControl = await _operatorA.PostAsync($"/api/incidents/{incident.Id}/risks/{risk.Id}/control", null);
        var afterFirst = (await firstControl.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Risks.Single();
        Assert.Equal(IncidentRiskStatus.Controlled, afterFirst.Status);
        Assert.NotNull(afterFirst.ReviewedAtUtc);
        Assert.Equal("Operator A", afterFirst.ReviewedByName);

        var secondControl = await _operatorA.PostAsync($"/api/incidents/{incident.Id}/risks/{risk.Id}/control", null);
        var afterSecond = (await secondControl.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Risks.Single();
        Assert.Equal(afterFirst.ReviewedAtUtc, afterSecond.ReviewedAtUtc); // second call didn't stomp on the first
    }

    [Fact]
    public async Task Reopening_a_controlled_risk_clears_the_stamp_and_a_later_control_is_fresh()
    {
        var incident = await CreateIncidentAsync("RISK-REOPEN-1");
        var addRisk = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/risks",
            new AddRiskRequest("Confined space entry required", IncidentRiskLevel.High, null));
        var risk = (await addRisk.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Risks.Single();

        await _operatorA.PostAsync($"/api/incidents/{incident.Id}/risks/{risk.Id}/control", null);

        var firstReopen = await _operatorA.PostAsync($"/api/incidents/{incident.Id}/risks/{risk.Id}/reopen", null);
        var afterFirstReopen = (await firstReopen.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Risks.Single();
        Assert.Equal(IncidentRiskStatus.Identified, afterFirstReopen.Status);
        Assert.Null(afterFirstReopen.ReviewedAtUtc);
        Assert.Null(afterFirstReopen.ReviewedByName);

        // Idempotent the same direction as control.
        var secondReopen = await _operatorA.PostAsync($"/api/incidents/{incident.Id}/risks/{risk.Id}/reopen", null);
        var afterSecondReopen = (await secondReopen.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Risks.Single();
        Assert.Equal(IncidentRiskStatus.Identified, afterSecondReopen.Status);

        // Controlling again after a reopen is a fresh review, not a
        // continuation of the cleared one.
        var reControl = await _operatorA.PostAsync($"/api/incidents/{incident.Id}/risks/{risk.Id}/control", null);
        var afterReControl = (await reControl.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Risks.Single();
        Assert.Equal(IncidentRiskStatus.Controlled, afterReControl.Status);
        Assert.NotNull(afterReControl.ReviewedAtUtc);
    }

    [Fact]
    public async Task Non_operator_cannot_raise_a_risk()
    {
        var incident = await CreateIncidentAsync("RISK-DENY-1");
        var crew = factory.AsUser(Seed.OrgA, Seed.CrewAPerson);

        var response = await crew.PostAsJsonAsync($"/api/incidents/{incident.Id}/risks",
            new AddRiskRequest("Not allowed", IncidentRiskLevel.Low, null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tablet_can_raise_and_control_a_risk_only_while_attending()
    {
        // Seed's own comment: DeviceA is attending IncidentA via its callsign.
        var tabletOnScene = factory.AsDevice(Seed.DeviceATokenPlaintext);
        var addRisk = await tabletOnScene.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/risks",
            new AddRiskRequest("Collapsed scaffolding", IncidentRiskLevel.High, "Crews withdrawn to safe distance"));
        addRisk.EnsureSuccessStatusCode();
        var risk = (await addRisk.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Risks
            .Single(r => r.Description == "Collapsed scaffolding");
        Assert.Equal(Seed.DeviceACallsign, risk.RaisedByName);

        var control = await tabletOnScene.PostAsync($"/api/tablet/incidents/{Seed.IncidentA}/risks/{risk.Id}/control", null);
        control.EnsureSuccessStatusCode();
        var controlled = (await control.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Risks.Single(r => r.Id == risk.Id);
        Assert.Equal(Seed.DeviceACallsign, controlled.ReviewedByName);

        // DeviceB is a different org's tablet and isn't attending IncidentA either way.
        var otherDevice = factory.AsDevice(Seed.DeviceBTokenPlaintext);
        var deniedAdd = await otherDevice.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/risks",
            new AddRiskRequest("Nope", IncidentRiskLevel.Low, null));
        Assert.Equal(HttpStatusCode.NotFound, deniedAdd.StatusCode);
        var deniedControl = await otherDevice.PostAsync($"/api/tablet/incidents/{Seed.IncidentA}/risks/{risk.Id}/control", null);
        Assert.Equal(HttpStatusCode.NotFound, deniedControl.StatusCode);
    }

    [Fact]
    public async Task Risks_are_ordered_by_creation_time()
    {
        var incident = await CreateIncidentAsync("RISK-ORDER-1");
        await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/risks", new AddRiskRequest("First", IncidentRiskLevel.Low, null));
        var afterSecond = await _operatorA.PostAsJsonAsync($"/api/incidents/{incident.Id}/risks", new AddRiskRequest("Second", IncidentRiskLevel.Low, null));
        var risks = (await afterSecond.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!.Risks;

        Assert.Equal(["First", "Second"], risks.Select(r => r.Description));
    }
}
