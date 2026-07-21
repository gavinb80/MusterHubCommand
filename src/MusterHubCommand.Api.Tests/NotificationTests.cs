using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;

namespace MusterHubCommand.Api.Tests;

// Regression coverage for the exact bug found via live verification:
// CoreNotificationService.NotifyStationEmployeesAsync queried
// EmployeeStationAssignments without IgnoreQueryFilters(), so the ambient
// ITenantScoped filter (which resolves to Guid.Empty for
// IntegrationIncidentsController's [AllowAnonymous] callers) silently
// returned zero employees for every Vision-pushed incident -- the relay
// was never even called, no exception, no log line, just nobody paged.
[Collection("Api")]
public class NotificationTests
{
    private readonly CommandApiFactory factory;
    private readonly HttpClient _integrationA;

    public NotificationTests(CommandApiFactory factory)
    {
        this.factory = factory;
        _integrationA = factory.AsIntegration(Seed.IntegrationKeyAPlaintext);
        // xunit constructs a fresh instance of this class per [Fact], but
        // FakeNotificationHandler.Requests is a static list spanning the
        // whole collection -- clear it here so each test only sees its own
        // pushes, not leftovers from IncidentLifecycleTests etc. sharing
        // the same collection fixture.
        FakeNotificationHandler.Requests.Clear();
    }

    [Fact]
    public async Task Creating_a_new_incident_pages_every_employee_at_that_station()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", new CreateIncidentRequest(
            "PUSH-NEW-1", "Chimney Fire", null, "5 Drake Road", null, null, "KV57", DateTimeOffset.UtcNow));

        // OrgA's StationA (Tavistock) has OperatorA and CrewA assigned --
        // both, and only those, should have been paged.
        var pagedIds = FakeNotificationHandler.Requests.Select(r => r.UserId).ToHashSet();
        Assert.Contains(Seed.OperatorAPerson, pagedIds);
        Assert.Contains(Seed.CrewAPerson, pagedIds);
        Assert.DoesNotContain(Seed.UnassignedAPerson, pagedIds); // has no station assignment
        Assert.DoesNotContain(Seed.OperatorBPerson, pagedIds); // different org entirely

        var toOperatorA = FakeNotificationHandler.Requests.Single(r => r.UserId == Seed.OperatorAPerson);
        Assert.Contains("Chimney Fire", toOperatorA.Title);
        Assert.Equal("test-notification-key", toOperatorA.ApiKeyHeader);
    }

    [Fact]
    public async Task Re_pushing_an_existing_incident_does_not_page_again()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", new CreateIncidentRequest(
            "PUSH-REPUSH-1", "RTC", null, "Original address", null, null, "KV57", DateTimeOffset.UtcNow));
        var countAfterCreate = FakeNotificationHandler.Requests.Count;
        Assert.True(countAfterCreate > 0);

        // Same ExternalReference, different address -- an update, not a
        // new incident -- must not re-page the whole crew's phones.
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", new CreateIncidentRequest(
            "PUSH-REPUSH-1", "RTC", null, "Amended address", null, null, "KV57", DateTimeOffset.UtcNow));

        Assert.Equal(countAfterCreate, FakeNotificationHandler.Requests.Count);
    }

    [Fact]
    public async Task Pushing_appliances_or_an_update_does_not_trigger_the_new_incident_push()
    {
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents", new CreateIncidentRequest(
            "PUSH-NOAPPLIANCE-1", "RTC", null, null, null, null, "KV57", DateTimeOffset.UtcNow));
        var countAfterCreate = FakeNotificationHandler.Requests.Count;

        await _integrationA.PutAsJsonAsync("/api/integrations/incidents/PUSH-NOAPPLIANCE-1/appliances",
            new SetAppliancesRequest([new SetApplianceEntry("P57P1", MusterHubCommand.Api.Data.Entities.ApplianceStatus.OnScene)]));
        await _integrationA.PostAsJsonAsync("/api/integrations/incidents/PUSH-NOAPPLIANCE-1/updates",
            new AddIncidentUpdateRequest("Control Room", "Hazard noted", MusterHubCommand.Api.Data.Entities.IncidentUpdateType.Hazard));

        Assert.Equal(countAfterCreate, FakeNotificationHandler.Requests.Count);
    }
}
