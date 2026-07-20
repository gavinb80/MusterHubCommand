namespace MusterHubCommandTablet.Services;

// No Settings screen to override this from -- unlike the main app's
// ApiConfig, a kiosk-paired tablet has no reason for a developer-facing
// override control. Same DEBUG-vs-prod default split as the main app's
// ApiConfig.BaseUrl / RotaAppUrl / SkillsAppUrl.
public static class AppConfig
{
    public const string SentryDsn = "";

    public static string ApiBaseUrl =>
#if DEBUG
#if ANDROID
        "http://10.0.2.2:5188";
#else
        "http://localhost:5188";
#endif
#else
        "https://musterhub-command.azurewebsites.net";
#endif
}
