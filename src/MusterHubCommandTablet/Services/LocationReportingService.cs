using Sentry;

namespace MusterHubCommandTablet.Services;

// Reports this tablet's own GPS position to the API every 60s, for the
// whole app lifetime -- not tied to any one page's poll timer, since the
// appliance's position needs to keep updating even while the crew is
// sitting on the Home screen waiting for a turnout. See Device.CurrentLatitude
// on the API side for why this is the tablet's own GPS, not a separate AVL
// feed. Started once from AppShell (already-paired restart) or
// PairingViewModel (first pair), never stopped -- a kiosk device has no
// backgrounding case to pause it for.
public class LocationReportingService(IApiClient apiClient, IDeviceTokenStore tokenStore)
{
    private IDispatcherTimer? timer;

    public void Start()
    {
        if (timer is not null) return;

        timer = Application.Current!.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(60);
        timer.Tick += async (_, _) => await ReportOnceAsync();
        timer.Start();

        _ = ReportOnceAsync();
    }

    private async Task ReportOnceAsync()
    {
        if (await tokenStore.GetAsync() is null) return;

        try
        {
            var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted) return;

            var location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(20)));
            if (location is null) return;

            await apiClient.ReportLocationAsync(location.Latitude, location.Longitude);
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
    }
}
