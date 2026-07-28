using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusterHubCommand.Api.Configuration;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Tests;

// One factory (and one test database) shared by every test class in the
// "Api" collection -- xunit runs classes in the same collection
// sequentially, so shared seed data isn't racing concurrent writers. Same
// shape as Rota/Skills' own ApiFactory.
public class CommandApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("COMMAND_TEST_DB")
        ?? "Host=localhost;Database=musterhubcommand_test;Username=musterhubcommandadmin;Password=MHC-dev-2026!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        // Never fetched -- TestAuthHandler replaces the JwtBearer scheme --
        // but Program.cs requires the section to exist at startup.
        builder.UseSetting("Core:JwksUri", "http://localhost:1/.well-known/jwks.json");
        builder.UseSetting("Core:Issuer", "MusterHub");
        builder.UseSetting("Core:Audience", "MusterHubApp");
        // Attachment bytes land in LocalFileStorage rooted at a temp dir,
        // not the real project directory -- same convention as Skills' own
        // test factory.
        builder.UseSetting("Storage:LocalPath", Path.Combine(Path.GetTempPath(), "musterhubcommand-test-attachments"));
        // Always configured (see the bottom of this method), so
        // CoreNotificationService.IsConfigured is true for every test, not
        // just NotificationTests -- every incident creation elsewhere in
        // the suite harmlessly records into FakeNotificationHandler.Requests
        // too, they just don't assert on it. NotificationTests clears that
        // static list in its own constructor so it only ever sees its own
        // pushes.

        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);

            // The import's upsert logic is what needs testing, not the HTTP
            // hop to core -- swap the client for one serving a canned payload.
            services.RemoveAll<ICoreDirectoryClient>();
            services.AddScoped<ICoreDirectoryClient, FakeCoreDirectoryClient>();

            // Records every outbound relay call instead of hitting a real
            // core instance -- NotificationTests asserts against
            // FakeNotificationHandler.Requests. Constructs
            // CoreNotificationService directly with a raw HttpClient
            // rather than intercepting Program.cs's own
            // AddHttpClient<CoreNotificationService> registration --
            // attempts to override that typed client's handler via
            // IHttpClientFactory's usual test hooks didn't reliably reach
            // it, so this sidesteps IHttpClientFactory entirely instead.
            services.RemoveAll<CoreNotificationService>();
            services.AddScoped(sp =>
            {
                var coreOptions = sp.GetRequiredService<IOptions<CoreAuthOptions>>().Value;
                var httpClient = new HttpClient(new FakeNotificationHandler()) { BaseAddress = new Uri("http://fake-core.test/") };
                if (!string.IsNullOrWhiteSpace(coreOptions.NotificationApiKey))
                    httpClient.DefaultRequestHeaders.Add("X-Api-Key", coreOptions.NotificationApiKey);

                return new CoreNotificationService(
                    sp.GetRequiredService<ApplicationDbContext>(),
                    httpClient,
                    sp.GetRequiredService<IOptions<CoreAuthOptions>>(),
                    sp.GetRequiredService<ILogger<CoreNotificationService>>());
            });
        });

        builder.UseSetting("Core:NotificationsUri", "http://fake-core.test/api/integrations/notifications");
        builder.UseSetting("Core:NotificationApiKey", "test-notification-key");
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options;
        await using (var db = new ApplicationDbContext(options, new FixedOrganisationAccessor(Guid.Empty)))
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
        }

        // Forces host startup, then seed through the real container so
        // OrgUnitPathInterceptor computes ltree paths exactly as
        // production writes do.
        _ = Server;

        using var scope = Services.CreateScope();
        var seedDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Seed.ApplyAsync(seedDb);
    }

    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    private sealed class FixedOrganisationAccessor(Guid organisationId) : ICurrentOrganisationAccessor
    {
        public Guid OrganisationId => organisationId;
    }
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<CommandApiFactory>;

// Two fully-populated organisations with deterministic IDs -- OrgA has a
// station, four employees (three with a station assignment, one without),
// one designated CommandOperator, a paired Device, and an IntegrationApiKey.
// OrgB is a second tenant with its own of everything, the strongest form of
// the isolation assertion since even OrgA's operator must never reach it.
public static class Seed
{
    public static readonly Guid OrgA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid OrgB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    // "OperatorAPerson" is an Incident Commander -- the elevated tier --
    // since every pre-existing test written before the tier split assumes
    // "the operator" can do everything (close/cancel, sector CRUD, grant
    // others), matching exactly what the migration backfills real
    // pre-existing grants to. CommandSupportAPerson/ControlRoomAPerson are
    // the two baseline tiers (same permissions, distinct named roles), for
    // tests that specifically assert the elevated-only actions are denied
    // to them.
    public static readonly Guid OperatorAPerson = Guid.Parse("aaaaaaaa-1111-0000-0000-000000000001");
    public static readonly Guid CrewAPerson = Guid.Parse("aaaaaaaa-1111-0000-0000-000000000002");
    public static readonly Guid UnassignedAPerson = Guid.Parse("aaaaaaaa-1111-0000-0000-000000000003");
    public static readonly Guid CommandSupportAPerson = Guid.Parse("aaaaaaaa-1111-0000-0000-000000000004");
    public static readonly Guid ControlRoomAPerson = Guid.Parse("aaaaaaaa-1111-0000-0000-000000000005");
    public static readonly Guid OperatorBPerson = Guid.Parse("bbbbbbbb-1111-0000-0000-000000000001");

    public static readonly Guid GroupTypeA = Guid.Parse("aaaaaaaa-2222-0000-0000-000000000001");
    public static readonly Guid StationTypeA = Guid.Parse("aaaaaaaa-2222-0000-0000-000000000002");
    public static readonly Guid GroupA = Guid.Parse("aaaaaaaa-3333-0000-0000-000000000001");
    public static readonly Guid StationA = Guid.Parse("aaaaaaaa-3333-0000-0000-000000000002");
    public static readonly Guid StationA2 = Guid.Parse("aaaaaaaa-3333-0000-0000-000000000003");

    public static readonly Guid GroupTypeB = Guid.Parse("bbbbbbbb-2222-0000-0000-000000000001");
    public static readonly Guid StationTypeB = Guid.Parse("bbbbbbbb-2222-0000-0000-000000000002");
    public static readonly Guid GroupB = Guid.Parse("bbbbbbbb-3333-0000-0000-000000000002");
    public static readonly Guid StationB = Guid.Parse("bbbbbbbb-3333-0000-0000-000000000001");

    public static readonly Guid OperatorAEmployee = Guid.Parse("aaaaaaaa-4444-0000-0000-000000000001");
    public static readonly Guid CrewAEmployee = Guid.Parse("aaaaaaaa-4444-0000-0000-000000000002");
    public static readonly Guid UnassignedAEmployee = Guid.Parse("aaaaaaaa-4444-0000-0000-000000000003");
    public static readonly Guid CommandSupportAEmployee = Guid.Parse("aaaaaaaa-4444-0000-0000-000000000004");
    public static readonly Guid ControlRoomAEmployee = Guid.Parse("aaaaaaaa-4444-0000-0000-000000000005");
    public static readonly Guid OperatorBEmployee = Guid.Parse("bbbbbbbb-4444-0000-0000-000000000001");

    public static readonly Guid IncidentA = Guid.Parse("aaaaaaaa-5555-0000-0000-000000000001");
    public static readonly Guid IncidentB = Guid.Parse("bbbbbbbb-5555-0000-0000-000000000001");

    // Known plaintexts so tests can authenticate as these directly -- only
    // the SHA-256 hash is ever persisted, same as production.
    public const string DeviceATokenPlaintext = "test-device-token-org-a";
    public const string DeviceBTokenPlaintext = "test-device-token-org-b";
    public const string IntegrationKeyAPlaintext = "test-integration-key-org-a";
    public const string IntegrationKeyBPlaintext = "test-integration-key-org-b";
    public static readonly Guid DeviceA = Guid.Parse("aaaaaaaa-6666-0000-0000-000000000001");
    public static readonly Guid DeviceB = Guid.Parse("bbbbbbbb-6666-0000-0000-000000000001");
    public const string DeviceACallsign = "KV57P1";

    // A second OrgA device, also attending IncidentA, so BA Entry Control's
    // per-device point ownership (one point per device, claim/hand-over
    // between devices) can be tested against two devices actually allowed
    // to see the same incident -- DeviceB is a different org entirely and
    // can't stand in for this.
    public const string DeviceA2TokenPlaintext = "test-device-token-org-a-2";
    public static readonly Guid DeviceA2 = Guid.Parse("aaaaaaaa-6666-0000-0000-000000000002");
    public const string DeviceA2Callsign = "KV57P9";

    public static async Task ApplyAsync(ApplicationDbContext db)
    {
        db.OrgUnitTypes.AddRange(
            new OrgUnitType { Id = GroupTypeA, OrganisationId = OrgA, Code = "GRP", Name = "Group" },
            new OrgUnitType { Id = StationTypeA, OrganisationId = OrgA, Code = "STN", Name = "Station", AllowedParentTypeId = GroupTypeA },
            new OrgUnitType { Id = GroupTypeB, OrganisationId = OrgB, Code = "GRP", Name = "Group" },
            new OrgUnitType { Id = StationTypeB, OrganisationId = OrgB, Code = "STN", Name = "Station", AllowedParentTypeId = GroupTypeB });

        db.OrgUnits.AddRange(
            new OrgUnit { Id = GroupA, OrganisationId = OrgA, OrgUnitTypeId = GroupTypeA, Name = "Plymouth" },
            new OrgUnit { Id = StationA, OrganisationId = OrgA, OrgUnitTypeId = StationTypeA, ParentId = GroupA, Name = "Tavistock", Code = "KV57" },
            new OrgUnit { Id = StationA2, OrganisationId = OrgA, OrgUnitTypeId = StationTypeA, ParentId = GroupA, Name = "Plympton", Code = "KV42" },
            new OrgUnit { Id = GroupB, OrganisationId = OrgB, OrgUnitTypeId = GroupTypeB, Name = "Exeter" },
            new OrgUnit { Id = StationB, OrganisationId = OrgB, OrgUnitTypeId = StationTypeB, ParentId = GroupB, Name = "Danes Castle", Code = "EX01" });

        db.Employees.AddRange(
            new Employee { Id = OperatorAEmployee, OrganisationId = OrgA, PersonId = OperatorAPerson, DisplayName = "Operator A" },
            new Employee { Id = CrewAEmployee, OrganisationId = OrgA, PersonId = CrewAPerson, DisplayName = "Crew A" },
            new Employee { Id = UnassignedAEmployee, OrganisationId = OrgA, PersonId = UnassignedAPerson, DisplayName = "Unassigned A" },
            new Employee { Id = CommandSupportAEmployee, OrganisationId = OrgA, PersonId = CommandSupportAPerson, DisplayName = "Command Support A" },
            new Employee { Id = ControlRoomAEmployee, OrganisationId = OrgA, PersonId = ControlRoomAPerson, DisplayName = "Control Room A" },
            new Employee { Id = OperatorBEmployee, OrganisationId = OrgB, PersonId = OperatorBPerson, DisplayName = "Operator B" });

        db.CommandOperators.Add(new CommandOperator { OrganisationId = OrgA, EmployeeId = OperatorAEmployee, Tier = CommandOperatorTier.IncidentCommander });
        db.CommandOperators.Add(new CommandOperator { OrganisationId = OrgA, EmployeeId = CommandSupportAEmployee, Tier = CommandOperatorTier.CommandSupport });
        db.CommandOperators.Add(new CommandOperator { OrganisationId = OrgA, EmployeeId = ControlRoomAEmployee, Tier = CommandOperatorTier.ControlRoom });
        db.CommandOperators.Add(new CommandOperator { OrganisationId = OrgB, EmployeeId = OperatorBEmployee, Tier = CommandOperatorTier.IncidentCommander });

        db.EmployeeStationAssignments.AddRange(
            new EmployeeStationAssignment { OrganisationId = OrgA, EmployeeId = OperatorAEmployee, OrgUnitId = StationA, IsHome = true },
            new EmployeeStationAssignment { OrganisationId = OrgA, EmployeeId = CrewAEmployee, OrgUnitId = StationA, IsHome = true },
            new EmployeeStationAssignment { OrganisationId = OrgB, EmployeeId = OperatorBEmployee, OrgUnitId = StationB, IsHome = true });

        db.Devices.Add(new Device { Id = DeviceA, OrganisationId = OrgA, OrgUnitId = StationA, Label = "Engine 1 (A)", Callsign = DeviceACallsign, TokenHash = SecretHasher.Hash(DeviceATokenPlaintext) });
        db.Devices.Add(new Device { Id = DeviceA2, OrganisationId = OrgA, OrgUnitId = StationA, Label = "Engine 2 (A)", Callsign = DeviceA2Callsign, TokenHash = SecretHasher.Hash(DeviceA2TokenPlaintext) });
        db.Devices.Add(new Device { Id = DeviceB, OrganisationId = OrgB, OrgUnitId = StationB, Label = "Engine 1 (B)", TokenHash = SecretHasher.Hash(DeviceBTokenPlaintext) });

        db.IntegrationApiKeys.Add(new IntegrationApiKey { OrganisationId = OrgA, Label = "Vision (A)", KeyHash = SecretHasher.Hash(IntegrationKeyAPlaintext) });
        db.IntegrationApiKeys.Add(new IntegrationApiKey { OrganisationId = OrgB, Label = "Vision (B)", KeyHash = SecretHasher.Hash(IntegrationKeyBPlaintext) });

        db.Incidents.Add(new Incident
        {
            Id = IncidentA, OrganisationId = OrgA, ExternalReference = "SEED-A-1", IncidentType = "RTC",
            Address = "Seed incident A", OrgUnitId = StationA, StartedAtUtc = DateTimeOffset.UtcNow,
        });
        db.Incidents.Add(new Incident
        {
            Id = IncidentB, OrganisationId = OrgB, ExternalReference = "SEED-B-1", IncidentType = "RTC",
            Address = "Seed incident B", OrgUnitId = StationB, StartedAtUtc = DateTimeOffset.UtcNow,
        });

        // DeviceA is "attending" IncidentA -- tablet endpoints now scope
        // visibility to the device's own callsign, not just its station
        // (see TabletIncidentsController.AttendedByThisDevice), so tests
        // that expect DeviceA to see IncidentA need this to actually hold.
        db.IncidentAppliances.Add(new IncidentAppliance { IncidentId = IncidentA, Callsign = DeviceACallsign });
        db.IncidentAppliances.Add(new IncidentAppliance { IncidentId = IncidentA, Callsign = DeviceA2Callsign });

        await db.SaveChangesAsync();
    }
}
