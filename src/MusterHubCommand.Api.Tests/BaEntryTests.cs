using System.Net;
using System.Net.Http.Json;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Tests;

// Only exercised against tablet endpoints -- BA Entry Control is a
// tablet-only write surface (see TabletIncidentsController's own comment),
// the web console only ever reads IncidentDto.BaEntryControlPoints.
[Collection("Api")]
public class BaEntryTests(CommandApiFactory factory)
{
    private readonly HttpClient _integrationA = factory.AsIntegration(Seed.IntegrationKeyAPlaintext);
    private readonly HttpClient _operatorA = factory.AsUser(Seed.OrgA, Seed.OperatorAPerson);

    private static CreateIncidentRequest NewIncident(string externalReference) => new(
        externalReference, "Structure Fire", "Two-storey dwelling", "A386, Tavistock", 50.5472, -4.1462, "KV57", DateTimeOffset.UtcNow);

    private async Task<IncidentDto> CreateIncidentAsync(string externalReference)
    {
        var response = await _integrationA.PostAsJsonAsync("/api/integrations/incidents", NewIncident(externalReference));
        return (await response.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
    }

    // OrganisationSettings is a singleton row shared across the whole "Api"
    // collection's database, same caveat GeofenceTests already works
    // around -- every test that turns BA on resets it off again afterward
    // so it can't leak into an unrelated test running later.
    private Task SetBaEntryControlEnabledAsync(bool enabled) =>
        _operatorA.PutAsJsonAsync("/api/organisation-settings", new UpdateOrganisationSettingsRequest(50, enabled));

    // A point's ownership isn't reset between facts the way
    // BaEntryControlEnabled is -- DeviceA/DeviceA2 are shared across every
    // test in this class, and once a device owns a point it can't create
    // or claim another. Every test that leaves one of the seeded devices
    // still owning a point at the end hands it back here so the next fact
    // (in whatever order xUnit runs them) starts from a clean, unclaimed
    // slate.
    private static Task HandBackAsync(HttpClient tablet, Guid? pointId) =>
        pointId is null ? Task.CompletedTask : tablet.PostAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points/{pointId}/hand-over", null);

    [Fact]
    public async Task Ba_point_endpoint_is_blocked_when_the_org_hasnt_enabled_it()
    {
        await SetBaEntryControlEnabledAsync(false);
        var tabletOnScene = factory.AsDevice(Seed.DeviceATokenPlaintext);

        var response = await tabletOnScene.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points",
            new AddBaEntryControlPointRequest("Alpha", BaStage.II));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Building_the_full_point_team_wearer_hierarchy_and_exiting_a_wearer_is_idempotent()
    {
        var tabletOnScene = factory.AsDevice(Seed.DeviceATokenPlaintext);
        Guid? pointId = null;
        await SetBaEntryControlEnabledAsync(true);
        try
        {
            var beforeAdd = DateTimeOffset.UtcNow;

            // Seed.IncidentA is shared across every fact in this class --
            // "Lifecycle-" prefixed names, and looking up by id/name rather
            // than a blind Single(), keep this test's own data apart from
            // whatever other facts have already added to the same incident.
            var addPoint = await tabletOnScene.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points",
                new AddBaEntryControlPointRequest("Lifecycle-Point", BaStage.II));
            Assert.Equal(HttpStatusCode.OK, addPoint.StatusCode);
            var afterPoint = (await addPoint.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
            var point = afterPoint.BaEntryControlPoints.Single(p => p.Name == "Lifecycle-Point");
            pointId = point.Id;
            Assert.Equal(BaStage.II, point.Stage);
            Assert.Empty(point.Teams);

            var addTeam = await tabletOnScene.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points/{point.Id}/teams",
                new AddBaTeamRequest("Lifecycle-Team", "FF Beard", "Channel 2", "Left hand direction search, search and rescue", "Hose reel, thermal imaging camera"));
            var afterTeam = (await addTeam.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
            var team = afterTeam.BaEntryControlPoints.Single(p => p.Id == point.Id).Teams.Single(t => t.Name == "Lifecycle-Team");
            Assert.Equal("FF Beard", team.TeamLeader);
            Assert.Equal("Channel 2", team.CommsChannel);
            Assert.Empty(team.Wearers);

            var addWearer = await tabletOnScene.PostAsJsonAsync(
                $"/api/tablet/incidents/{Seed.IncidentA}/ba-points/{point.Id}/teams/{team.Id}/wearers",
                new AddBaWearerRequest("FF Beard", 232, 20));
            var afterWearer = (await addWearer.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
            var wearer = afterWearer.BaEntryControlPoints.Single(p => p.Id == point.Id).Teams.Single(t => t.Id == team.Id).Wearers.Single();
            Assert.Equal("FF Beard", wearer.Name);
            Assert.Equal(232, wearer.CylinderPressureBar);
            Assert.Equal(BaWearerStatus.InBa, wearer.Status);
            Assert.Null(wearer.ExitedAtUtc);

            // WhistleAtUtc is resolved from the server's own clock against
            // the minutes-from-now the request sent, not a caller-supplied
            // timestamp -- a tablet's clock isn't trustworthy enough to
            // compute one that gets persisted.
            Assert.InRange(wearer.WhistleAtUtc, beforeAdd.AddMinutes(20), DateTimeOffset.UtcNow.AddMinutes(20).AddSeconds(5));

            var firstExit = await tabletOnScene.PostAsync(
                $"/api/tablet/incidents/{Seed.IncidentA}/ba-points/{point.Id}/teams/{team.Id}/wearers/{wearer.Id}/exit", null);
            var afterFirstExit = (await firstExit.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Id == point.Id).Teams.Single(t => t.Id == team.Id).Wearers.Single(w => w.Id == wearer.Id);
            Assert.Equal(BaWearerStatus.Exited, afterFirstExit.Status);
            Assert.NotNull(afterFirstExit.ExitedAtUtc);

            // Idempotent -- a second exit call doesn't stomp on the first timestamp.
            var secondExit = await tabletOnScene.PostAsync(
                $"/api/tablet/incidents/{Seed.IncidentA}/ba-points/{point.Id}/teams/{team.Id}/wearers/{wearer.Id}/exit", null);
            var afterSecondExit = (await secondExit.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Id == point.Id).Teams.Single(t => t.Id == team.Id).Wearers.Single(w => w.Id == wearer.Id);
            Assert.Equal(afterFirstExit.ExitedAtUtc, afterSecondExit.ExitedAtUtc);
        }
        finally
        {
            await HandBackAsync(tabletOnScene, pointId);
            await SetBaEntryControlEnabledAsync(false);
        }
    }

    [Fact]
    public async Task An_incident_can_run_more_than_one_entry_control_point_at_once()
    {
        // One point per device (see the ownership tests below), so two
        // points at once needs two different devices -- DeviceA2 is
        // OrgA's second seeded tablet, also attending IncidentA.
        var tabletA = factory.AsDevice(Seed.DeviceATokenPlaintext);
        var tabletA2 = factory.AsDevice(Seed.DeviceA2TokenPlaintext);
        Guid? point1Id = null, point2Id = null;
        await SetBaEntryControlEnabledAsync(true);
        try
        {
            // Seed.IncidentA is shared across every fact in this class, so
            // names here are unique to this test rather than reused
            // "Alpha"/"Bravo" -- otherwise a later assertion can't tell its
            // own points apart from ones an earlier fact already added.
            var addMulti1 = await tabletA.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points",
                new AddBaEntryControlPointRequest("Multi-Alpha", BaStage.II));
            var point1 = (await addMulti1.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Name == "Multi-Alpha");
            point1Id = point1.Id;
            var addMulti2 = await tabletA2.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points",
                new AddBaEntryControlPointRequest("Multi-Bravo", BaStage.I));
            var point2 = (await addMulti2.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Name == "Multi-Bravo");
            point2Id = point2.Id;

            // The web console isn't device-scoped -- it sees every point on
            // the incident regardless of which device owns it.
            var webView = (await (await _operatorA.GetAsync($"/api/incidents/{Seed.IncidentA}")).Content
                .ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
            Assert.Contains(webView.BaEntryControlPoints, p => p.Id == point1.Id);
            Assert.Contains(webView.BaEntryControlPoints, p => p.Id == point2.Id);
        }
        finally
        {
            await HandBackAsync(tabletA, point1Id);
            await HandBackAsync(tabletA2, point2Id);
            await SetBaEntryControlEnabledAsync(false);
        }
    }

    [Fact]
    public async Task A_device_only_sees_its_own_point_and_unclaimed_points_not_another_devices()
    {
        var tabletA = factory.AsDevice(Seed.DeviceATokenPlaintext);
        var tabletA2 = factory.AsDevice(Seed.DeviceA2TokenPlaintext);
        Guid? mineId = null, theirsId = null;
        await SetBaEntryControlEnabledAsync(true);
        try
        {
            var addMine = await tabletA.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points",
                new AddBaEntryControlPointRequest("Scoped-Mine", BaStage.II));
            var mine = (await addMine.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Name == "Scoped-Mine");
            mineId = mine.Id;
            Assert.True(mine.IsOwnedByThisDevice);

            var addTheirs = await tabletA2.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points",
                new AddBaEntryControlPointRequest("Scoped-Theirs", BaStage.I));
            Assert.Equal(HttpStatusCode.OK, addTheirs.StatusCode);
            theirsId = (await addTheirs.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Name == "Scoped-Theirs").Id;

            // DeviceA already owns "Scoped-Mine" -- its own view is scoped
            // down to just that one point, "Scoped-Theirs" isn't in it even
            // though it's on the same incident.
            var deviceAView = (await (await tabletA.GetAsync($"/api/tablet/incidents/{Seed.IncidentA}")).Content
                .ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
            Assert.Contains(deviceAView.BaEntryControlPoints, p => p.Name == "Scoped-Mine");
            Assert.DoesNotContain(deviceAView.BaEntryControlPoints, p => p.Name == "Scoped-Theirs");
        }
        finally
        {
            await HandBackAsync(tabletA, mineId);
            await HandBackAsync(tabletA2, theirsId);
            await SetBaEntryControlEnabledAsync(false);
        }
    }

    [Fact]
    public async Task A_device_that_already_owns_a_point_cannot_create_or_claim_another()
    {
        var tabletA = factory.AsDevice(Seed.DeviceATokenPlaintext);
        var tabletA2 = factory.AsDevice(Seed.DeviceA2TokenPlaintext);
        Guid? firstId = null;
        await SetBaEntryControlEnabledAsync(true);
        try
        {
            var addFirst = await tabletA.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points",
                new AddBaEntryControlPointRequest("OneOnly-First", BaStage.II));
            var first = (await addFirst.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Name == "OneOnly-First");
            firstId = first.Id;

            var addSecond = await tabletA.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points",
                new AddBaEntryControlPointRequest("OneOnly-Second", BaStage.I));
            Assert.Equal(HttpStatusCode.BadRequest, addSecond.StatusCode);

            // A separate, still-unclaimed point exists (created by the
            // other device) -- DeviceA still can't claim it while it
            // already owns "OneOnly-First".
            var addUnclaimed = await tabletA2.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points",
                new AddBaEntryControlPointRequest("OneOnly-Unclaimed", BaStage.III));
            var unclaimed = (await addUnclaimed.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Name == "OneOnly-Unclaimed");
            var handOver = await tabletA2.PostAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points/{unclaimed.Id}/hand-over", null);
            Assert.Equal(HttpStatusCode.OK, handOver.StatusCode);

            var deniedClaim = await tabletA.PostAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points/{unclaimed.Id}/claim", null);
            Assert.Equal(HttpStatusCode.BadRequest, deniedClaim.StatusCode);

            // Sanity: DeviceA still only owns its original point.
            var deviceAView = (await (await tabletA.GetAsync($"/api/tablet/incidents/{Seed.IncidentA}")).Content
                .ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
            Assert.Single(deviceAView.BaEntryControlPoints.Where(p => p.IsOwnedByThisDevice));
            Assert.Equal(first.Id, deviceAView.BaEntryControlPoints.Single(p => p.IsOwnedByThisDevice).Id);
        }
        finally
        {
            await HandBackAsync(tabletA, firstId);
            await SetBaEntryControlEnabledAsync(false);
        }
    }

    [Fact]
    public async Task Handing_over_a_point_lets_a_different_device_claim_it_and_then_add_teams()
    {
        var tabletA = factory.AsDevice(Seed.DeviceATokenPlaintext);
        var tabletA2 = factory.AsDevice(Seed.DeviceA2TokenPlaintext);
        Guid? pointId = null;
        await SetBaEntryControlEnabledAsync(true);
        try
        {
            var addPoint = await tabletA.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points",
                new AddBaEntryControlPointRequest("HandOver-Point", BaStage.II));
            var point = (await addPoint.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Name == "HandOver-Point");
            pointId = point.Id;

            // The new owner can't add a team before the handover/claim.
            var deniedTeam = await tabletA2.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points/{point.Id}/teams",
                new AddBaTeamRequest("Too-Early-Team", "FF Ahead"));
            var deniedResult = (await deniedTeam.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
            Assert.DoesNotContain(deniedResult.BaEntryControlPoints.SelectMany(p => p.Teams), t => t.Name == "Too-Early-Team");

            var handOver = await tabletA.PostAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points/{point.Id}/hand-over", null);
            Assert.Equal(HttpStatusCode.OK, handOver.StatusCode);

            var claim = await tabletA2.PostAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points/{point.Id}/claim", null);
            var claimed = (await claim.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Id == point.Id);
            Assert.True(claimed.IsOwnedByThisDevice);

            // The original device no longer owns or sees it.
            var deviceAView = (await (await tabletA.GetAsync($"/api/tablet/incidents/{Seed.IncidentA}")).Content
                .ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!;
            Assert.DoesNotContain(deviceAView.BaEntryControlPoints, p => p.Id == point.Id);

            var addTeam = await tabletA2.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points/{point.Id}/teams",
                new AddBaTeamRequest("HandOver-Team", "FF Okonkwo"));
            var team = (await addTeam.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Id == point.Id).Teams.Single(t => t.Name == "HandOver-Team");
            Assert.Equal("FF Okonkwo", team.TeamLeader);
        }
        finally
        {
            // Ownership ended up with tabletA2 after the claim.
            await HandBackAsync(tabletA2, pointId);
            await SetBaEntryControlEnabledAsync(false);
        }
    }

    [Fact]
    public async Task Comms_channel_briefing_and_equipment_are_optional_on_a_team()
    {
        var tabletOnScene = factory.AsDevice(Seed.DeviceATokenPlaintext);
        Guid? pointId = null;
        await SetBaEntryControlEnabledAsync(true);
        try
        {
            var addPoint = await tabletOnScene.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points",
                new AddBaEntryControlPointRequest("Optional-Point", BaStage.II));
            var point = (await addPoint.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Name == "Optional-Point");
            pointId = point.Id;

            var addTeam = await tabletOnScene.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points/{point.Id}/teams",
                new AddBaTeamRequest("Optional-Team", "FF Okafor"));
            var team = (await addTeam.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Id == point.Id).Teams.Single(t => t.Name == "Optional-Team");

            Assert.Null(team.CommsChannel);
            Assert.Null(team.Briefing);
            Assert.Null(team.Equipment);
        }
        finally
        {
            await HandBackAsync(tabletOnScene, pointId);
            await SetBaEntryControlEnabledAsync(false);
        }
    }

    [Fact]
    public async Task An_entry_past_its_whistle_time_with_no_exit_reads_as_overdue()
    {
        var tabletOnScene = factory.AsDevice(Seed.DeviceATokenPlaintext);
        Guid? pointId = null;
        await SetBaEntryControlEnabledAsync(true);
        try
        {
            var addPoint = await tabletOnScene.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points",
                new AddBaEntryControlPointRequest("Overdue-Point", BaStage.III));
            var point = (await addPoint.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Name == "Overdue-Point");
            pointId = point.Id;
            var addTeam = await tabletOnScene.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points/{point.Id}/teams",
                new AddBaTeamRequest("Overdue-Team", "FF Patel"));
            var team = (await addTeam.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Id == point.Id).Teams.Single(t => t.Name == "Overdue-Team");

            // A negative minutes-from-now -- no need to wait out a real
            // whistle duration to exercise the Overdue computation.
            var addWearer = await tabletOnScene.PostAsJsonAsync(
                $"/api/tablet/incidents/{Seed.IncidentA}/ba-points/{point.Id}/teams/{team.Id}/wearers",
                new AddBaWearerRequest("FF Nguyen", 220, -1));
            var wearer = (await addWearer.Content.ReadFromJsonAsync<IncidentDto>(ClientExtensions.Json))!
                .BaEntryControlPoints.Single(p => p.Id == point.Id).Teams.Single(t => t.Id == team.Id).Wearers.Single();

            Assert.Equal(BaWearerStatus.Overdue, wearer.Status);
        }
        finally
        {
            await HandBackAsync(tabletOnScene, pointId);
            await SetBaEntryControlEnabledAsync(false);
        }
    }

    [Fact]
    public async Task Ba_endpoints_only_work_for_the_device_attending_the_incident()
    {
        await SetBaEntryControlEnabledAsync(true);
        try
        {
            // DeviceB is a different org's tablet and isn't attending IncidentA either way.
            var otherDevice = factory.AsDevice(Seed.DeviceBTokenPlaintext);
            var deniedAdd = await otherDevice.PostAsJsonAsync($"/api/tablet/incidents/{Seed.IncidentA}/ba-points",
                new AddBaEntryControlPointRequest("Nope", BaStage.I));
            Assert.Equal(HttpStatusCode.NotFound, deniedAdd.StatusCode);
        }
        finally
        {
            await SetBaEntryControlEnabledAsync(false);
        }
    }
}
