using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusterHubCommandTablet.Models;
using MusterHubCommandTablet.Services;
using Sentry;

namespace MusterHubCommandTablet.ViewModels;

// View-model-only, unlike Models/IncidentModels.cs's DTOs -- this doesn't
// mirror anything on the API, it's IncidentDetailViewModel's own merge of
// Incident.StartedAtUtc/ClosedAtUtc with the real IncidentUpdateDto rows
// into one bindable, chronological list (see BuildTimeline below).
public record TimelineEntry(string Id, string Kind, string Text, string Caption, DateTimeOffset Timestamp);

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
    private List<TimelineEntry> timeline = [];

    // Session-only, defaults visible -- resets on every fresh entry to this
    // page rather than persisting, same as the web console's own toggle.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMap))]
    [NotifyPropertyChangedFor(nameof(MapToggleLabel))]
    private bool isMapVisible = true;

    public bool ShowMap => HasLocation && IsMapVisible;
    public string MapToggleLabel => IsMapVisible ? "Hide map" : "Show map";

    [ObservableProperty]
    private string noteText = string.Empty;

    [ObservableProperty]
    private bool isPostingNote;

    [ObservableProperty]
    private bool isStartingNavigation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RouteScript))]
    [NotifyPropertyChangedFor(nameof(RouteSummary))]
    private RouteResponseDto? route;

    // Org-wide, essentially static -- fetched once, not on every 15s poll.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MapSource))]
    private double? geofenceRadiusMeters;

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

    // Merges Incident's own creation/close timestamps with the real
    // IncidentUpdateDto rows (which by this point already include a
    // ResourceChange entry for every appliance status change --
    // IncidentService writes those server-side) into one chronological,
    // oldest-first list. Same merge the web console's own buildTimeline
    // does, kept in sync by hand rather than shared -- two small, separate
    // client-side projections of the same wire data, not worth a shared
    // package for.
    private static List<TimelineEntry> BuildTimeline(IncidentDto incident)
    {
        var entries = new List<TimelineEntry>
        {
            new("created", "Created", $"Incident created: {incident.IncidentType}", incident.OrgUnitName, incident.StartedAtUtc),
        };
        entries.AddRange(incident.Updates.Select(u => new TimelineEntry(
            u.Id.ToString(), u.UpdateType,
            u.Text,
            // authorName first regardless of source -- a tablet's crew note
            // is tagged with its own device callsign
            // (TabletIncidentsController.AddNote), so this reads as
            // "KV57P1", not a bare "Crew note" indistinguishable from every
            // other appliance's. Only a null authorName (a resource change
            // auto-logged with no note text of its own) falls back to the
            // generic per-source label.
            u.UpdateType == "Hazard" ? "HAZARD" : (u.AuthorName ?? (u.Source == "Crew" ? "Crew" : "Control Room")),
            u.CreatedAtUtc)));
        if (incident.ClosedAtUtc is { } closedAt) entries.Add(new("closed", "Closed", "Incident closed", incident.Status, closedAt));

        return entries.OrderBy(e => e.Timestamp).ToList();
    }

    [RelayCommand]
    private void ToggleMap() => IsMapVisible = !IsMapVisible;

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
            Url = $"map/index.html?lat={Incident!.Latitude!.Value.ToString(CultureInfo.InvariantCulture)}&lng={Incident.Longitude!.Value.ToString(CultureInfo.InvariantCulture)}" +
                  (GeofenceRadiusMeters is { } r ? $"&radius={r.ToString(CultureInfo.InvariantCulture)}" : ""),
        }
        : null;

    public async Task OnAppearingAsync()
    {
        await RefreshAsync();
        _ = LoadGeofenceRadiusAsync();

        refreshTimer ??= Application.Current!.Dispatcher.CreateTimer();
        refreshTimer.Interval = TimeSpan.FromSeconds(15);
        refreshTimer.Tick += async (_, _) => await RefreshAsync();
        refreshTimer.Start();
    }

    private async Task LoadGeofenceRadiusAsync()
    {
        try
        {
            var (result, error) = await apiClient.GetOrganisationSettingsAsync();
            if (error != ApiClient.RevokedError && result is not null) GeofenceRadiusMeters = result.GeofenceRadiusMeters;
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
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
            Timeline = BuildTimeline(result);
            OnPropertyChanged(nameof(HasLocation));
            OnPropertyChanged(nameof(ShowMap));
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
            // Without this, a posted note doesn't show up in the Timeline
            // card until the next 15s poll -- confirmed live: the field
            // clears (a successful post) but the crew sees nothing change,
            // which reads as "did that actually work?" for up to 15s.
            Timeline = BuildTimeline(result);
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

    // Navigates immediately rather than awaiting the attendance-update call
    // first -- that call is already best-effort/a no-op API-side (no
    // Callsign set), so blocking the transition to the driving screen on it
    // too just adds a second network round-trip of dead time on top of
    // NavigateViewModel's own (GPS fix, report, route). Fired instead, its
    // own failure only logs, never surfaces here -- the crew is already
    // looking at the nav screen by the time it would resolve.
    [RelayCommand]
    private async Task StartNavigationAsync()
    {
        if (!HasLocation || IsStartingNavigation) return;
        IsStartingNavigation = true;
        try
        {
            _ = UpdateAttendanceStatusAsync();
            await Shell.Current.GoToAsync($"navigate?id={IncidentId}");
        }
        finally
        {
            IsStartingNavigation = false;
        }
    }

    private async Task UpdateAttendanceStatusAsync()
    {
        try
        {
            var (result, error) = await apiClient.StartNavigationAsync(IncidentId);
            if (result is not null)
            {
                Incident = result;
                Timeline = BuildTimeline(result);
            }
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
    }

    public void Dispose()
    {
        if (refreshTimer is not null) refreshTimer.Stop();
        GC.SuppressFinalize(this);
    }
}
