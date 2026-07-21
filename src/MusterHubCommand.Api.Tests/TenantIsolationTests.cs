using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;

namespace MusterHubCommand.Api.Tests;

// OrgA's Operator is fully privileged *within A* -- the strongest form of
// the isolation assertion, since even an org-wide grant must never cross
// the tenant boundary. Covers both the JWT (web console) and the two
// non-JWT surfaces (Device, X-Api-Key), since the anonymous-caller
// tenant-filter bug found this session specifically affected the ones that
// don't carry an ambient organisation.
[Collection("Api")]
public class TenantIsolationTests(CommandApiFactory factory)
{
    private readonly HttpClient _operatorA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);

    [Fact]
    public async Task OrgA_operator_cannot_read_OrgBs_incident()
    {
        var response = await _operatorA.GetAsync($"/api/incidents/{Seed.IncidentB}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OrgA_operator_cannot_read_OrgBs_org_units_or_employees()
    {
        var orgUnits = await _operatorA.GetListAsync<OrgUnitDto>("/api/org-units");
        Assert.DoesNotContain(orgUnits, u => u.Id == Seed.StationB);

        var employees = await _operatorA.GetListAsync<EmployeeDto>("/api/employees");
        Assert.DoesNotContain(employees, e => e.Id == Seed.OperatorBEmployee);
    }

    [Fact]
    public async Task OrgBs_device_token_cannot_see_OrgAs_incident()
    {
        var deviceB = factory.AsDevice(Seed.DeviceBTokenPlaintext);
        var response = await deviceB.GetAsync($"/api/tablet/incidents/{Seed.IncidentA}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OrgBs_integration_key_cannot_reach_OrgAs_incident_by_external_reference()
    {
        var integrationB = factory.AsIntegration(Seed.IntegrationKeyBPlaintext);
        var response = await integrationB.PatchAsJsonAsync($"/api/integrations/incidents/SEED-A-1",
            new { description = "Should not be reachable from org B" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_organisations_device_token_used_against_the_web_endpoints_gets_401_not_the_JWT_scheme()
    {
        // A Device token is never valid on a JWT-only endpoint, regardless
        // of which org it belongs to -- the two schemes are never
        // interchangeable.
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Seed.DeviceATokenPlaintext);
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
