using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Tests;

[Collection("Api")]
public class CoreImportTests(CommandApiFactory factory)
{
    private readonly HttpClient _operatorA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);

    private static readonly Guid CorePersonId = Guid.NewGuid();

    [Fact]
    public async Task Import_rejects_a_key_belonging_to_a_different_organisation()
    {
        FakeCoreDirectoryClient.RejectKey = false;
        FakeCoreDirectoryClient.Payload = new CoreDirectory(Seed.OrgB, "Wrong Org", [], [], []); // OrgB, caller is OrgA

        var response = await _operatorA.PostAsJsonAsync("/api/import/core-directory", new { apiKey = "test-key" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Import_rejects_a_key_core_itself_rejects()
    {
        FakeCoreDirectoryClient.RejectKey = true;
        var response = await _operatorA.PostAsJsonAsync("/api/import/core-directory", new { apiKey = "bad-key" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        FakeCoreDirectoryClient.RejectKey = false;
    }

    // Station membership sync is replace-whole per employee, not date-
    // ranged accumulation: this is the deliberate divergence from Rota/
    // Skills' own EmployeeStationAssignment import, which only ever adds
    // new memberships and never closes stale ones out. Command's whole
    // reason for this table is "who gets paged for an incident here right
    // now" -- copying that accumulate-only behaviour would leave someone
    // who transferred stations still getting paged at their old one
    // indefinitely.
    [Fact]
    public async Task A_second_sync_that_drops_a_station_removes_the_stale_assignment()
    {
        var firstGroupId = Guid.NewGuid();
        var firstStationId = Guid.NewGuid();

        FakeCoreDirectoryClient.RejectKey = false;
        FakeCoreDirectoryClient.Payload = new CoreDirectory(
            Seed.OrgA, "Devon & Somerset",
            [new CoreGroup(firstGroupId, "Plymouth Group")],
            [new CoreStation(firstStationId, "SYNC1", "Sync Station", firstGroupId)],
            [new CoreUser(CorePersonId, "Transferring Firefighter", "FF", null,
                [new CoreUserStation(firstStationId, true)])]);

        var first = await _operatorA.PostAsJsonAsync("/api/import/core-directory", new { apiKey = "test-key" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var employees = await _operatorA.GetListAsync<EmployeeDto>("/api/employees");
        Assert.Contains(employees, e => e.DisplayName == "Transferring Firefighter");

        // Second sync: same person, now at a DIFFERENT station, with no
        // trace of the first one -- exactly what "transferred stations"
        // looks like from core's export.
        var secondGroupId = Guid.NewGuid();
        var secondStationId = Guid.NewGuid();
        FakeCoreDirectoryClient.Payload = new CoreDirectory(
            Seed.OrgA, "Devon & Somerset",
            [new CoreGroup(secondGroupId, "Exeter Group")],
            [new CoreStation(secondStationId, "SYNC2", "Other Station", secondGroupId)],
            [new CoreUser(CorePersonId, "Transferring Firefighter", "FF", null,
                [new CoreUserStation(secondStationId, true)])]);

        var second = await _operatorA.PostAsJsonAsync("/api/import/core-directory", new { apiKey = "test-key" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var summary = (await second.Content.ReadFromJsonAsync<CoreDirectoryImportSummary>(ClientExtensions.Json))!;
        Assert.True(summary.StationMembershipsChanged >= 2); // one removed, one added

        // The stale membership must be gone, not just superseded: pushing
        // an incident to the NEW station must page this person, proving
        // the import actually resolved and kept the current membership
        // (not just that the old one silently vanished).
        var integrationA = factory.AsIntegration(Seed.IntegrationKeyAPlaintext);
        var push = await integrationA.PostAsJsonAsync("/api/integrations/incidents", new CreateIncidentRequest(
            "TRANSFER-CHECK-1", "RTC", null, null, null, null, "SYNC2", DateTimeOffset.UtcNow));
        Assert.Equal(HttpStatusCode.OK, push.StatusCode);

        var paged = FakeNotificationHandler.Requests.Select(r => r.UserId).ToHashSet();
        Assert.Contains(CorePersonId, paged);
    }
}
