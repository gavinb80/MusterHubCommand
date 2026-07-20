namespace MusterHubCommandTablet.Services;

// Persists the paired device's bearer token in platform secure storage --
// same "hold it in SecureStorage, never prompt again" shape as the main
// app's TokenStore, except there's no refresh flow: a device token is
// long-lived by design (see DeviceAuthenticationHandler on the API side)
// and only ever becomes invalid if an Operator revokes it from Setup.
public class DeviceTokenStore : IDeviceTokenStore
{
    private const string TokenKey = "command_device_token";

    public async Task SaveAsync(string token)
    {
        try
        {
            await SecureStorage.Default.SetAsync(TokenKey, token);
        }
        catch (Exception)
        {
#if DEBUG
            // Unsigned/ad-hoc builds can't write to the Keychain locally --
            // same fallback as the main app's TokenStore, dev-only.
            Preferences.Default.Set(TokenKey, token);
#endif
        }
    }

    public async Task<string?> GetAsync()
    {
        try
        {
            var token = await SecureStorage.Default.GetAsync(TokenKey);
            if (!string.IsNullOrEmpty(token)) return token;
        }
        catch (Exception) { }

#if DEBUG
        var fallback = Preferences.Default.Get(TokenKey, (string?)null);
        if (!string.IsNullOrEmpty(fallback)) return fallback;
#endif

        return null;
    }

    public Task ClearAsync()
    {
        try { SecureStorage.Default.Remove(TokenKey); } catch (Exception) { }
#if DEBUG
        Preferences.Default.Remove(TokenKey);
#endif
        return Task.CompletedTask;
    }
}
