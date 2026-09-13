using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Tests;

// Covers EnsureModuleAdminGrantedAsync, the mechanism that closes the real
// bootstrap gap: the core directory import creates Employee rows with no
// CommandOperator grant, so a fresh org's bootstrap window (see
// BootstrapTests) would otherwise stay open indefinitely for a real org
// until a human happens to use the Operators tab. Core's module_admin
// claim (set when a core admin ticks "Command" for someone in Admin Users
// Edit) lets that first grant happen automatically the moment the
// designated person signs into Command. Same shape as Rota/Skills' own
// ModuleAdminAutoProvisionTests.
[Collection("Api")]
public class ModuleAdminAutoProvisionTests(CommandApiFactory factory)
{
    // One sequential story, for the same reason as BootstrapTests' own --
    // the auto-grant's whole point is that it happens in place of the
    // manual first grant, so the steps only mean anything in order.
    [Fact]
    public async Task Designated_module_admin_auto_closes_bootstrap_on_first_me_call_with_an_employee()
    {
        var orgId = Guid.NewGuid();
        var founderPerson = Guid.NewGuid();
        var secondPerson = Guid.NewGuid();
        var founder = factory.AsUser(orgId, founderPerson, moduleAdmin: true);

        // No Employee yet: the claim alone can't grant anything, since
        // EnsureModuleAdminGrantedAsync needs an employeeId to assign a
        // grant to. Bootstrap covers this window the same way it always has.
        var meBefore = await (await founder.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, meBefore.GetProperty("employeeId").ValueKind);
        Assert.True(meBefore.GetProperty("isBootstrapping").GetBoolean());

        var stationId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        FakeCoreDirectoryClient.RejectKey = false;
        FakeCoreDirectoryClient.Payload = new CoreDirectory(
            orgId, "Fresh Org D",
            [new CoreGroup(groupId, "Group D")],
            [new CoreStation(stationId, "GD1", "Station D1", groupId)],
            [new CoreUser(founderPerson, "Founder", "Firefighter", null, [new CoreUserStation(stationId, true)])]);

        var import = await founder.PostAsJsonAsync("/api/import/core-directory", new { apiKey = "test-key" });
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        var summary = (await import.Content.ReadFromJsonAsync<CoreDirectoryImportSummary>(ClientExtensions.Json))!;
        Assert.Equal(1, summary.EmployeesCreated);

        // This /api/me call is where the grant happens -- no
        // /api/employees/{id}/operator call was ever made, unlike the
        // manual flow in BootstrapTests.
        var meAfterImport = await (await founder.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(meAfterImport.GetProperty("isBootstrapping").GetBoolean());
        Assert.True(meAfterImport.GetProperty("isIncidentCommander").GetBoolean());

        // The window closed for the whole org, not just for the founder: a
        // second person with no claim and no grant is genuinely denied now.
        var second = factory.AsUser(orgId, secondPerson);
        var meSecond = await (await second.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(meSecond.GetProperty("isBootstrapping").GetBoolean());
        Assert.False(meSecond.GetProperty("isOperator").GetBoolean());

        // Idempotent: calling /api/me again as the founder must not create
        // a second CommandOperator row.
        await founder.GetAsync("/api/me");
        await founder.GetAsync("/api/me");

        var employees = await founder.GetListAsync<EmployeeDto>("/api/employees");
        Assert.Single(employees, e => e.OperatorTier == CommandOperatorTier.IncidentCommander);
    }

    // Distinct from the bootstrap story above: OrgA's bootstrap window is
    // already closed (Seed grants OperatorAEmployee Incident Commander),
    // proving EnsureModuleAdminGrantedAsync only grants to someone who
    // doesn't already hold an operator grant of their own, rather than
    // assuming a fresh org.
    [Fact]
    public async Task Designated_module_admin_is_granted_incident_commander_in_an_already_bootstrapped_org()
    {
        var meBefore = await (await factory.AsUser(Seed.OrgA, Seed.UnassignedAPerson).GetAsync("/api/me"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(meBefore.GetProperty("isOperator").GetBoolean());

        var newAdmin = factory.AsUser(Seed.OrgA, Seed.UnassignedAPerson, moduleAdmin: true);
        var meAfter = await (await newAdmin.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(meAfter.GetProperty("isOperator").GetBoolean());
        Assert.True(meAfter.GetProperty("isIncidentCommander").GetBoolean());

        var operatorA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);
        var employees = await operatorA.GetListAsync<EmployeeDto>("/api/employees");
        Assert.Contains(employees, e => e.Id == Seed.UnassignedAEmployee && e.OperatorTier == CommandOperatorTier.IncidentCommander);

        // A plain crew member with no claim, in the same org, is untouched.
        var meOther = await (await factory.AsUser(Seed.OrgA, Seed.CrewAPerson).GetAsync("/api/me"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(meOther.GetProperty("isOperator").GetBoolean());
    }
}
