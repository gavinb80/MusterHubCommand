using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Tests;

[Collection("Api")]
public class BootstrapTests(CommandApiFactory factory)
{
    // One sequential story, deliberately a single Fact: the bootstrap
    // window's whole point is its lifecycle (open for a fresh org, self-
    // closes on the first CommandOperator grant) and the steps only mean
    // anything in order. This is the exact scenario that was broken before
    // the RequireOperatorAsync ordering fix: the very first directory
    // import for a fresh org -- the call that creates the founder's
    // Employee record in the first place -- used to 403 permanently,
    // because it checked "does this caller have a linked Employee" before
    // "is this org still bootstrapping."
    [Fact]
    public async Task Bootstrap_window_is_open_for_a_fresh_org_and_self_closes_on_first_operator_grant()
    {
        var orgC = Guid.NewGuid();
        var founderPerson = Guid.NewGuid();
        var secondPerson = Guid.NewGuid();
        var founder = factory.AsUser(orgC, founderPerson);

        // Before the founder even has an Employee record: /api/me must
        // still report isOperator=true, or nobody could ever reach the
        // import that's the only way to create that first Employee record
        // at all.
        var meBefore = await (await founder.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, meBefore.GetProperty("employeeId").ValueKind);
        Assert.True(meBefore.GetProperty("isOperator").GetBoolean());
        Assert.True(meBefore.GetProperty("isBootstrapping").GetBoolean());

        var stationId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        FakeCoreDirectoryClient.RejectKey = false;
        FakeCoreDirectoryClient.Payload = new CoreDirectory(
            orgC, "Fresh Org C",
            [new CoreGroup(groupId, "Group C")],
            [new CoreStation(stationId, "GC1", "Station C1", groupId)],
            [new CoreUser(founderPerson, "Founder", "Firefighter", null, [new CoreUserStation(stationId, true)])]);

        var import = await founder.PostAsJsonAsync("/api/import/core-directory", new { apiKey = "test-key" });
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        var summary = (await import.Content.ReadFromJsonAsync<CoreDirectoryImportSummary>(ClientExtensions.Json))!;
        Assert.Equal(1, summary.EmployeesCreated);

        var meAfterImport = await (await founder.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>();
        var founderEmployeeId = meAfterImport.GetProperty("employeeId").GetGuid();
        Assert.True(meAfterImport.GetProperty("isBootstrapping").GetBoolean()); // still open -- no grant yet

        // The founder grants themselves Incident Commander -- this closes
        // the window. (Bootstrapping treats them as a Commander already, so
        // this self-grant is itself allowed by the same escape hatch.)
        var grant = await founder.PutAsJsonAsync($"/api/employees/{founderEmployeeId}/operator", new SetOperatorRequest(CommandOperatorTier.IncidentCommander));
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);

        var meAfterGrant = await (await founder.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(meAfterGrant.GetProperty("isBootstrapping").GetBoolean());

        // OrgUnit.Id is always freshly generated on import (stationId above
        // only ever lands in OrgUnit.CoreId) -- look up the station's real
        // local id rather than assuming the two coincide.
        var orgUnits = await founder.GetListAsync<OrgUnitDto>("/api/org-units");
        var localStationId = orgUnits.Single(u => u.Code == "GC1").Id;

        // ...so a second orgC user with no grant is now genuinely denied.
        var second = factory.AsUser(orgC, secondPerson);
        var denied = await second.PostAsJsonAsync("/api/devices", new { label = "Should be denied", orgUnitId = localStationId });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // But the founder, now a real Operator, can still act.
        var allowed = await founder.PostAsJsonAsync("/api/devices", new { label = "Engine 1", orgUnitId = localStationId });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task RequireCommandEntitlement_rejects_an_identity_whose_org_has_no_command_entitlement()
    {
        var client = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson, entitled: false);
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_request_is_401_not_403()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
