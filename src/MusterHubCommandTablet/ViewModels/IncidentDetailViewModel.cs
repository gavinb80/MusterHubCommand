using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusterHubCommandTablet.Models;
using MusterHubCommandTablet.Services;
using Sentry;

namespace MusterHubCommandTablet.ViewModels;

[QueryProperty(nameof(IncidentIdString), "id")]
public partial class IncidentDetailViewModel : BaseViewModel, IDisposable
{
    private readonly IApiClient apiClient;
    private IDispatcherTimer? refreshTimer;

    public IncidentDetailViewModel(IApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    [ObservableProperty]
    private Guid incidentId;

    // Shell's QueryProperty machinery hands query values to the target
    // property via Convert.ChangeType, which has no string->Guid
    // conversion and throws -- so this has to be the string Shell sets,
    // parsed into the real IncidentId ourselves, not IncidentId directly.
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
    private string noteText = string.Empty;

    [ObservableProperty]
    private bool isPostingNote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RouteScript))]
    [NotifyPropertyChangedFor(nameof(RouteSummary))]
    private RouteResponseDto? route;

    public bool HasLocation => Incident?.Latitude is not null && Incident?.Longitude is not null;

    // "139m · under a minute", or the API's own explanation when there's no
    // route yet (no GPS reported, no path found) -- same wording convention
    // as the web console's RoutingPanel.
    public string? RouteSummary => Route switch
    {
        { Available: true, DistanceMeters: { } distance, DurationSeconds: { } duration } =>
            $"{FormatDistance(distance)} · {FormatDuration(duration)}",
        { Available: false } => Route.UnavailableReason,
        _ => null,
    };

    // The JS call IncidentDetailPage's code-behind evaluates against the
    // bundled Leaflet page whenever a fresh route comes back -- keeping the
    // string-building here, not in code-behind, is what keeps that class
    // down to a plain native WebView shim.
    public string RouteScript
    {
        get
        {
            if (Route is not { Available: true, Points.Count: > 0 } route) return "clearRoute();";

            var appliance = route.Points[0];
            var pointsJson = JsonSerializer.Serialize(route.Points.Select(p => new[] { p.Latitude, p.Longitude }));
            return $"setRoute({appliance.Latitude.ToString(CultureInfo.InvariantCulture)}, " +
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

    // The bundled Leaflet page (Resources/Raw/map/index.html) reads lat/lng
    // off its own query string -- simplest way to hand data into a local
    // WebView page without a JS-eval bridge.
    public UrlWebViewSource? MapSource => HasLocation
        ? new UrlWebViewSource
        {
            Url = $"map/index.html?lat={Incident!.Latitude!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lng={Incident.Longitude!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
        }
        : null;

    public async Task OnAppearingAsync()
    {
        await RefreshAsync();

        refreshTimer ??= Application.Current!.Dispatcher.CreateTimer();
        refreshTimer.Interval = TimeSpan.FromSeconds(15);
        refreshTimer.Tick += async (_, _) => await RefreshAsync();
        refreshTimer.Start();
    }

    public void OnDisappearing() => refreshTimer?.Stop();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy || IncidentId == Guid.Empty) return;
        IsBusy = true;
        try
        {
            var (result, error) = await apiClient.GetIncidentAsync(IncidentId);

            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }

            if (result is null)
            {
                ErrorMessage = error ?? "Couldn't load this incident.";
                return;
            }

            ErrorMessage = string.Empty;
            // Only re-notify MapSource when the coordinates actually moved --
            // this fires on every 15s poll, and reassigning WebView.Source
            // unconditionally would reload the map (losing the crew's pan/
            // zoom) even when nothing about the location changed.
            var locationChanged = Incident?.Latitude != result.Latitude || Incident?.Longitude != result.Longitude;
            Incident = result;
            OnPropertyChanged(nameof(HasLocation));
            if (locationChanged) OnPropertyChanged(nameof(MapSource));

            if (HasLocation)
            {
                var (routeResult, routeError) = await apiClient.GetRouteAsync(IncidentId);
                if (routeError != ApiClient.RevokedError) Route = routeResult;
            }
            else
            {
                Route = null;
            }
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong loading this incident.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddNoteAsync()
    {
        if (string.IsNullOrWhiteSpace(NoteText) || IsPostingNote) return;

        IsPostingNote = true;
        try
        {
            var (result, error) = await apiClient.AddNoteAsync(IncidentId, NoteText.Trim());

            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }

            if (result is null)
            {
                ErrorMessage = error ?? "Couldn't post that note. Try again.";
                return;
            }

            ErrorMessage = string.Empty;
            Incident = result;
            NoteText = string.Empty;
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong posting that note.";
        }
        finally
        {
            IsPostingNote = false;
        }
    }

    [RelayCommand]
    private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");

    public void Dispose()
    {
        if (refreshTimer is not null) refreshTimer.Stop();
        GC.SuppressFinalize(this);
    }
}
