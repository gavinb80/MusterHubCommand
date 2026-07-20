namespace MusterHubCommandTablet.Services;

public interface IDeviceTokenStore
{
    Task SaveAsync(string token);
    Task<string?> GetAsync();
    Task ClearAsync();
}
