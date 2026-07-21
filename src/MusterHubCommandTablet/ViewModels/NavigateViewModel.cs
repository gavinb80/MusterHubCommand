using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusterHubCommandTablet.Models;
using MusterHubCommandTablet.Services;
using Sentry;

namespace MusterHubCommandTablet.ViewModels;

// Full-screen driving view, entered from IncidentDetailViewModel's "Start
// navigation". Runs its own faster (~12s) poll loop independent of
// LocationReportingService's normal 60s background one -- simpler than
// reconfiguring a shared singleton's interval, and a harmless occasional
// redundant report. Recomputes the WHOLE route from the appliance's current
// live position on every poll (the same GetRouteAsync the incident detail
// screen uses), which is what keeps the turn-by-turn banner naturally
// "current" without any separate maneuver-tracking logic of its own.
[QueryProperty(nameof(IncidentIdString), "id")]
public partial class NavigateViewModel : BaseViewModel, IDisposable
{
    private readonly IApiClient apiClient;
    private IDispatcherTimer? navTimer;

    public NavigateViewModel(IApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    [ObservableProperty]
    private Guid incidentId;

    // Same Convert.ChangeType crash Shell's QueryProperty hits on a
    // Guid-typed target -- see IncidentDetailViewModel.IncidentIdString.
    public string IncidentIdString
    {
        get => IncidentId.ToString();
        set
        {
            if (Guid.TryParse(value, out var parsed)) IncidentId = parsed;
        }
    }

    [ObservableProperty]
    private IncidentDto? incident;

    // Org-wide, essentially static -- fetched once alongside the incident,
    // not on every ~12s poll.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MapSource))]
    private double? geofenceRadiusMeters;

    // True only for the very first load (incident fetch, GPS fix, first
    // route computation) -- distinct from the base IsBusy flag PollOnceAsync
    // also sets on every subsequent ~12s poll, which must NOT re-show a
    // full-screen overlay every cycle while the crew is mid-drive.
    [ObservableProperty]
    private bool isLoadingRoute = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RouteSummary))]
    [NotifyPropertyChangedFor(nameof(CurrentInstructionText))]
    [NotifyPropertyChangedFor(nameof(RouteScript))]
    private RouteResponseDto? route;

    // North-up by default -- resets every fresh navigate-mode entry, no
    // persisted preference for v1. GPS direction of travel, not the device
    // compass: this tablet is mounted in a metal vehicle cab, where a
    // magnetometer is notoriously unreliable, and heading only matters
    // while actually driving anyway.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeadingScript))]
    [NotifyPropertyChangedFor(nameof(OrientationToggleLabel))]
    private bool isHeadingUp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeadingScript))]
    private double? heading;

    private double? lastFixLatitude;
    private double? lastFixLongitude;

    public bool HasLocation => Incident?.Latitude is not null && Incident?.Longitude is not null;

    public UrlWebViewSource? MapSource => HasLocation
        ? new UrlWebViewSource
        {
            Url = $"map/index.html?lat={Incident!.Latitude!.Value.ToString(CultureInfo.InvariantCulture)}&lng={Incident.Longitude!.Value.ToString(CultureInfo.InvariantCulture)}" +
                  (GeofenceRadiusMeters is { } r ? $"&radius={r.ToString(CultureInfo.InvariantCulture)}" : ""),
        }
        : null;

    public string? RouteSummary => Route switch
    {
        { Available: true, DistanceMeters: { } distance, DurationSeconds: { } duration } =>
            $"{FormatDistance(distance)} · {FormatDuration(duration)}",
        { Available: false } => Route.UnavailableReason,
        _ => null,
    };

    // Always the first instruction in the freshly-recomputed list -- since
    // the whole route is rebuilt from the live position every poll, that's
    // always "the next maneuver from here," not a stale one from further
    // back down the road.
    public string? CurrentInstructionText => Route?.Instructions?.FirstOrDefault()?.Text;

    public string RouteScript
    {
        get
        {
            if (Route is not { Available: true, Points.Count: > 0 } route) return "clearRoute();";

            var appliance = route.Points[0];
            var pointsJson = JsonSerializer.Serialize(route.Points.Select(p => new[] { p.Latitude, p.Longitude }));
            return $"setNavRoute({appliance.Latitude.ToString(CultureInfo.InvariantCulture)}, " +
                   $"{appliance.Longitude.ToString(CultureInfo.InvariantCulture)}, {pointsJson});";
        }
    }

    private static string FormatDistance(double metres) =>
        metres >= 1000 ? $"{(metres / 1000).ToString("0.0", CultureInfo.InvariantCulture)}km" : $"{Math.Round(metres)}m";

    private static string FormatDuration(double seconds)
    {
        var minutes = Math.Round(seconds / 60);
        return minutes < 1 ? "under a minute" : $"{minutes} min";
    }

    public string OrientationToggleLabel => IsHeadingUp ? "Heading up" : "North up";

    // setNorthUp() when off, or setHeadingUp() only once a heading is
    // actually known -- toggling on before the first usable fix just
    // leaves the map as-is rather than rotating to a meaningless 0.
    public string HeadingScript => IsHeadingUp && Heading is { } h
        ? $"setHeadingUp({h.ToString(CultureInfo.InvariantCulture)});"
        : "setNorthUp();";

    [RelayCommand]
    private void ToggleOrientation() => IsHeadingUp = !IsHeadingUp;

    // Deliberately NOT using the platform's own reported Location.Course --
    // confirmed live on the Android emulator that it reports a constant 0
    // regardless of actual movement, rather than being null when
    // meaningless (exactly the risk flagged before building this: unverified
    // platform behaviour). Always computed from the last fix to this one
    // instead, but only once they're far enough apart (~8m) that the
    // bearing means something -- GPS noise makes it jitter wildly at low
    // speed or near-standstill. If not far enough apart yet, Heading is
    // left untouched rather than snapped to 0 or a noisy value.
    private void UpdateHeading(Location location)
    {
        if (lastFixLatitude is { } prevLat && lastFixLongitude is { } prevLng &&
            DistanceMeters(prevLat, prevLng, location.Latitude, location.Longitude) >= 8)
        {
            Heading = CalculateBearing(prevLat, prevLng, location.Latitude, location.Longitude);
        }

        lastFixLatitude = location.Latitude;
        lastFixLongitude = location.Longitude;
    }

    private static double CalculateBearing(double lat1, double lng1, double lat2, double lng2)
    {
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var deltaLambda = (lng2 - lng1) * Math.PI / 180;

        var y = Math.Sin(deltaLambda) * Math.Cos(phi2);
        var x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(deltaLambda);
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    private static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusMetres = 6371000;
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var deltaPhi = (lat2 - lat1) * Math.PI / 180;
        var deltaLambda = (lng2 - lng1) * Math.PI / 180;

        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        return earthRadiusMetres * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public async Task OnAppearingAsync()
    {
        DeviceDisplay.Current.KeepScreenOn = true;
        IsLoadingRoute = true;

        try
        {
            var incidentTask = apiClient.GetIncidentAsync(IncidentId);
            var settingsTask = apiClient.GetOrganisationSettingsAsync();
            await Task.WhenAll(incidentTask, settingsTask);

            var (result, error) = incidentTask.Result;
            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }

            var (settings, settingsError) = settingsTask.Result;
            if (settingsError != ApiClient.RevokedError && settings is not null) GeofenceRadiusMeters = settings.GeofenceRadiusMeters;

            if (result is not null)
            {
                Incident = result;
                OnPropertyChanged(nameof(HasLocation));
                OnPropertyChanged(nameof(MapSource));
            }

            await PollOnceAsync();
        }
        finally
        {
            IsLoadingRoute = false;
        }

        navTimer ??= Application.Current!.Dispatcher.CreateTimer();
        navTimer.Interval = TimeSpan.FromSeconds(12);
        navTimer.Tick += async (_, _) => await PollOnceAsync();
        navTimer.Start();
    }

    public void OnDisappearing()
    {
        DeviceDisplay.Current.KeepScreenOn = false;
        navTimer?.Stop();
    }

    private async Task PollOnceAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)));
            if (location is not null)
            {
                await apiClient.ReportLocationAsync(location.Latitude, location.Longitude);
                UpdateHeading(location);
            }

            var (routeResult, routeError) = await apiClient.GetRouteAsync(IncidentId);
            if (routeError != ApiClient.RevokedError) Route = routeResult;
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task EndNavigationAsync() => await Shell.Current.GoToAsync("..");

    public void Dispose()
    {
        navTimer?.Stop();
        GC.SuppressFinalize(this);
    }
}
