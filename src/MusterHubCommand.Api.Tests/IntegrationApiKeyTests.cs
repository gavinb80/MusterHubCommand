using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;

namespace MusterHubCommand.Api.Tests;

[Collection("Api")]
public class IntegrationApiKeyTests(CommandApiFactory factory)
{
    private readonly HttpClient _operatorA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);

    [Fact]
    public async Task Issued_key_works_immediately_and_is_shown_only_once()
    {
        var create = await _operatorA.PostAsJsonAsync("/api/integration-api-keys", new { label = "Vision (issued in test)" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var issued = (await create.Content.ReadFromJsonAsync<CreateIntegrationApiKeyResponse>(ClientExtensions.Json))!;
        Assert.False(string.IsNullOrWhiteSpace(issued.ApiKey));

        var pushIncident = await factory.AsIntegration(issued.ApiKey).PostAsJsonAsync("/api/integrations/incidents",
            new CreateIncidentRequest("ISSUED-KEY-1", "RTC", null, null, null, null, "KV57", null));
        Assert.Equal(HttpStatusCode.OK, pushIncident.StatusCode);

        var list = await _operatorA.GetListAsync<IntegrationApiKeyDto>("/api/integration-api-keys");
        var listed = list.Single(k => k.Id == issued.Id);
        Assert.True(listed.IsActive);
        Assert.NotNull(listed.LastUsedAtUtc); // stamped by the push above
    }

    [Fact]
    public async Task Revoked_key_stops_working_immediately()
    {
        var create = await _operatorA.PostAsJsonAsync("/api/integration-api-keys", new { label = "To be revoked" });
        var issued = (await create.Content.ReadFromJsonAsync<CreateIntegrationApiKeyResponse>(ClientExtensions.Json))!;

        var revoke = await _operatorA.DeleteAsync($"/api/integration-api-keys/{issued.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var pushAfterRevoke = await factory.AsIntegration(issued.ApiKey).PostAsJsonAsync("/api/integrations/incidents",
            new CreateIncidentRequest("REVOKED-KEY-1", "RTC", null, null, null, null, "KV57", null));
        Assert.Equal(HttpStatusCode.Unauthorized, pushAfterRevoke.StatusCode);
    }

    [Fact]
    public async Task Non_operator_cannot_issue_a_key()
    {
        var crew = factory.AsUser(Seed.OrgA, Seed.CrewAPerson);
        var response = await crew.PostAsJsonAsync("/api/integration-api-keys", new { label = "Should be denied" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
