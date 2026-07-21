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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RouteSummary))]
    [NotifyPropertyChangedFor(nameof(CurrentInstructionText))]
    [NotifyPropertyChangedFor(nameof(RouteScript))]
    private RouteResponseDto? route;

    public bool HasLocation => Incident?.Latitude is not null && Incident?.Longitude is not null;

    public UrlWebViewSource? MapSource => HasLocation
        ? new UrlWebViewSource
        {
            Url = $"map/index.html?lat={Incident!.Latitude!.Value.ToString(CultureInfo.InvariantCulture)}&lng={Incident.Longitude!.Value.ToString(CultureInfo.InvariantCulture)}",
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

    public async Task OnAppearingAsync()
    {
        DeviceDisplay.Current.KeepScreenOn = true;

        var (result, error) = await apiClient.GetIncidentAsync(IncidentId);
        if (error == ApiClient.RevokedError)
        {
            await Shell.Current.GoToAsync("//pairing");
            return;
        }
        if (result is not null)
        {
            Incident = result;
            OnPropertyChanged(nameof(HasLocation));
            OnPropertyChanged(nameof(MapSource));
        }

        await PollOnceAsync();

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
                await apiClient.ReportLocationAsync(location.Latitude, location.Longitude);

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
