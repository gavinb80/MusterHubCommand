namespace MusterHubCommand.Api.Configuration;

// Identity for the control-room WEB console is entirely borrowed from
// MusterHub core -- Command never mints its own tokens and never holds
// core's private signing key, only where to fetch its public JWKS and which
// issuer/audience to expect. The tablet app is deliberately NOT part of this
// -- it authenticates with its own long-lived Device token instead (see
// Services/DeviceAuthenticationHandler.cs), which has no relationship to
// core's JWT chain at all.
public class CoreAuthOptions
{
    public const string SectionName = "Core";

    public required string JwksUri { get; set; }
    public required string Issuer { get; set; }
    public required string Audience { get; set; }

    // Both optional: unset (tests, fresh dev setups) disables the
    // "new incident at your station" phone push entirely -- CoreNotificationService no-ops.
    public string? NotificationsUri { get; set; }
    public string? NotificationApiKey { get; set; }
}
