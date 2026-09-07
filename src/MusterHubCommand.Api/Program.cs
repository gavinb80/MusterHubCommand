using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using MusterHubCommand.Api.Configuration;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Hubs;
using MusterHubCommand.Api.Routing;
using MusterHubCommand.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Error reporting only lights up when a DSN is configured (prod app
// setting Sentry__Dsn) -- same opt-in shape as Rota/Skills.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Sentry:Dsn"]))
{
    builder.WebHost.UseSentry();
}

// String enums, not ordinal ints: unlike the rest of this codebase's
// internal APIs, IntegrationIncidentsController is a contract external
// teams (Vision) integrate against directly -- "OnScene" surviving a future
// reordering of ApplianceStatus is worth the wire-format inconsistency with
// Rota/Skills' own int-enum convention.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

builder.Services.AddSingleton<OrgUnitPathInterceptor>();
builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"), npgsql => npgsql.EnableRetryOnFailure())
        .AddInterceptors(services.GetRequiredService<OrgUnitPathInterceptor>()));

builder.Services.AddScoped<ICurrentOrganisationAccessor, HttpContextCurrentOrganisationAccessor>();
builder.Services.AddScoped<ICurrentEmployeeAccessor, HttpContextCurrentEmployeeAccessor>();
builder.Services.AddScoped<OperatorPermissionChecker>();
builder.Services.AddScoped<IncidentService>();
builder.Services.AddScoped<GeofenceService>();
builder.Services.AddScoped<IntegrationApiKeyValidator>();

builder.Services.AddScoped<CoreDirectoryImportService>();
builder.Services.AddHttpClient<ICoreDirectoryClient, HttpCoreDirectoryClient>();

// Attachment bytes: Azure Blob via managed identity when Storage:BlobEndpoint
// is set (prod), plain local files otherwise (dev/tests) -- no connection
// string, no secret, either way. Same pattern as Rota/Skills.
var blobEndpoint = builder.Configuration["Storage:BlobEndpoint"];
if (!string.IsNullOrWhiteSpace(blobEndpoint))
{
    var containerName = builder.Configuration["Storage:Container"] ?? "incident-media";
    builder.Services.AddSingleton<IFileStorage>(new AzureBlobFileStorage(blobEndpoint, containerName));
}
else
{
    var localPath = builder.Configuration["Storage:LocalPath"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "attachments");
    builder.Services.AddSingleton<IFileStorage>(new LocalFileStorage(localPath));
}

// Loads its routerdb once and stays a singleton for the app's lifetime --
// deserializing an OSM-derived RouterDb isn't cheap, and Router/RouterDb
// are meant to be shared across requests.
builder.Services.Configure<RoutingOptions>(builder.Configuration.GetSection(RoutingOptions.SectionName));
builder.Services.AddSingleton<RoutingService>();

// Nominatim's usage policy requires a request identifying the calling
// application (User-Agent or Referer) and expects occasional, human-
// triggered lookups rather than bulk traffic -- exactly what the "Locate"
// button is.
builder.Services.AddHttpClient<GeocodingService>(client =>
{
    client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MusterHubCommand/1.0 (musterhub.app)");
    client.Timeout = TimeSpan.FromSeconds(8);
});

builder.Services.AddHttpClient<CoreNotificationService>((sp, client) =>
{
    client.Timeout = TimeSpan.FromSeconds(5); // best-effort: never hold up incident creation
    var options = sp.GetRequiredService<IOptions<CoreAuthOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.NotificationApiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", options.NotificationApiKey);
});
builder.Services.AddScoped<DirectorySyncJob>();

builder.Services.Configure<CoreAuthOptions>(builder.Configuration.GetSection(CoreAuthOptions.SectionName));
builder.Services.AddHttpContextAccessor();

// Postgres-backed, not Redis: the nightly directory sync job needs retries
// and safe multi-instance locking, but the workload is light enough that a
// second datastore isn't worth it -- same reasoning as Rota's own Hangfire setup.
builder.Services.AddHangfire(config => config
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Default"))));
builder.Services.AddHangfireServer();

// One probe that proves the whole stack: process up, DB reachable.
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default")!);

// Per-IP fixed window, same shape as Rota/Skills.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        ctx.Connection.RemoteIpAddress is { } ip
            ? System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                ip.ToString(),
                _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                {
                    PermitLimit = 300,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                })
            : System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("in-process"));
});

var coreAuthOptions = builder.Configuration.GetSection(CoreAuthOptions.SectionName).Get<CoreAuthOptions>()
    ?? throw new InvalidOperationException("Core configuration section is missing.");

// Caches core's JWKS and refreshes it automatically -- same shape as
// Rota/Skills' own ConfigurationManager<JsonWebKeySet> setup.
var jwksConfigManager = new ConfigurationManager<JsonWebKeySet>(
    coreAuthOptions.JwksUri,
    new CoreJwksConfigurationRetriever(),
    new HttpDocumentRetriever { RequireHttps = !builder.Environment.IsDevelopment() });

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = coreAuthOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = coreAuthOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeyResolver = (_, _, kid, _) =>
            {
                var keySet = jwksConfigManager.GetConfigurationAsync().GetAwaiter().GetResult();
                return kid is null ? keySet.GetSigningKeys() : keySet.GetSigningKeys().Where(k => k.KeyId == kid);
            },
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        // Browsers can't set an Authorization header on a WebSocket
        // handshake, so the SignalR JS client passes the token as a query
        // string param instead (see IncidentHub) -- standard ASP.NET Core
        // pattern, scoped to the hub path only so every other JWT-bearer
        // request still needs a real header.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Path.StartsWithSegments("/hubs") &&
                    context.Request.Query.TryGetValue("access_token", out var token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            },
        };
    })
    // The tablet's parallel scheme -- registered alongside JWT bearer, never
    // the default, only ever reached via DeviceControllerBase's explicit
    // AuthenticationSchemes = "Device".
    .AddScheme<DeviceAuthenticationSchemeOptions, DeviceAuthenticationHandler>(
        DeviceAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
    // Entitlement, not authentication: a valid MusterHub identity that
    // hasn't been granted the Command module still gets a 403 here, distinct
    // from a 401 for "not signed in at all". Only ever checked for the JWT
    // (web console) scheme -- CommandControllerBase's policy, never applied
    // to Device-authenticated tablet endpoints.
    options.AddPolicy("RequireCommandEntitlement", policy =>
        policy.RequireAssertion(ctx => ctx.User.HasClaim(c => c.Type == "entitlements" && c.Value == "command")));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRateLimiter();

// Same audited CSP as Rota/Skills -- a Vite-built SPA (script-src 'self',
// no inline scripts), React's style={{}} needs style-src 'unsafe-inline',
// Google Fonts and the OSM tile subdomains (IncidentMap.tsx's Leaflet
// layer) are the only cross-origin fetches, everything else same-origin.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    context.Response.Headers["Permissions-Policy"] =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=(), " +
        "magnetometer=(), accelerometer=(), gyroscope=(), fullscreen=(self)";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "img-src 'self' data: https://*.tile.openstreetmap.org; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self';";
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<IncidentHub>("/hubs/incidents");
app.MapHealthChecks("/health");

// The built React app (musterhub-command-web -- the control-room console
// only; the tablet is a separate MAUI app, not served from here) is copied
// into wwwroot at publish time.
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

using (var scope = app.Services.CreateScope())
{
    var recurringJobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurringJobs.AddOrUpdate<DirectorySyncJob>(
        "core-directory-sync",
        job => job.RunAsync(CancellationToken.None),
        Cron.Daily);
}

app.Run();

// WebApplicationFactory<Program> needs a visible entry-point type.
public partial class Program;
