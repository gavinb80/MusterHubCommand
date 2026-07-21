using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MusterHubCommand.Api.Tests;

public static class ClientExtensions
{
    // Must match Program.cs's own JsonStringEnumConverter registration --
    // the server sends IncidentStatus/ApplianceStatus/etc. as strings, not
    // ordinal ints, since IntegrationIncidentsController is a contract
    // external teams integrate against directly.
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // One HttpClient per identity: header-based auth means identity is
    // per-request state, and separate clients keep a test's "as the
    // operator" and "as unassigned crew" calls from contaminating each
    // other.
    public static HttpClient AsUser(this CommandApiFactory factory, Guid orgId, Guid? personId = null, bool entitled = true)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.OrgIdHeader, orgId.ToString());
        if (personId is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.PersonIdHeader, personId.Value.ToString());
        if (!entitled) client.DefaultRequestHeaders.Add(TestAuthHandler.NoEntitlementHeader, "1");
        return client;
    }

    // The tablet's own scheme -- a real bearer token validated against the
    // seeded Device row, no test-only substitute.
    public static HttpClient AsDevice(this CommandApiFactory factory, string tokenPlaintext)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenPlaintext);
        return client;
    }

    // Vision's own integration surface -- AllowAnonymous with a real
    // X-Api-Key header, validated against the seeded IntegrationApiKey row.
    public static HttpClient AsIntegration(this CommandApiFactory factory, string apiKeyPlaintext)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKeyPlaintext);
        return client;
    }

    public static async Task<List<T>> GetListAsync<T>(this HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<T>>(Json))!;
    }
}
