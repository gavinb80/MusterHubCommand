using System.Collections.ObjectModel;
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
public record TimelineEntry(
    string Id, string Kind, string Text, string Caption, DateTimeOffset Timestamp,
    bool Acknowledgeable, DateTimeOffset? AcknowledgedAtUtc, string? AcknowledgedByName,
    bool CanReply, string? ReplyToText);

// Read-only on the tablet -- crews see which sector each appliance is in,
// but creating sectors and assigning appliances to them stays a
// control-room action, same as attendance/status changes already are.
// Plain List<T>-derived rather than a bindable collection: unlike Timeline,
// this doesn't scroll independently, so a wholesale rebind on every poll
// has no scroll-position to lose.
public class ApplianceGroup(string sectorName, IEnumerable<IncidentApplianceDto> appliances) : List<IncidentApplianceDto>(appliances)
{
    public string SectorName { get; } = sectorName;
}

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

    public string FormattedAddress => Incident?.Address?.TrimEnd(',', ' ') ?? string.Empty;

    public string IncidentNumberLabel => Incident is null ? string.Empty : $"#{Incident.ExternalReference}";

    public string StartedLabel => Incident is null ? string.Empty : $"Started {Incident.StartedAtUtc.ToLocalTime():d MMM HH:mm}";

    // Fixed instance, never reassigned -- SyncTimeline below updates it in
    // place. Confirmed live: rebinding a fresh List<TimelineEntry> to
    // ItemsSource on every 15s poll reset the CollectionView's scroll
    // position to the top every time, which is exactly the kind of thing
    // that makes a crew stop trusting the auto-refresh. Same fix as
    // ActiveIncidentsViewModel.SyncIncidents already applies to the
    // incident list, for the same reason.
    public ObservableCollection<TimelineEntry> Timeline { get; } = [];

    // Sectors first in SortOrder, "Unassigned" last, empty groups dropped --
    // same grouping the web console's own AttendancePanel does. Recomputed
    // wholesale on every poll (see ApplianceGroup's own comment on why that's
    // fine here unlike Timeline).
    public List<ApplianceGroup> ApplianceGroups => BuildApplianceGroups();

    private List<ApplianceGroup> BuildApplianceGroups()
    {
        if (Incident is null) return [];

        var groups = Incident.Sectors
            .OrderBy(s => s.SortOrder)
            .Select(s => new ApplianceGroup(s.Name, Incident.Appliances.Where(a => a.SectorId == s.Id)))
            .Where(g => g.Count > 0)
            .ToList();

        var unassigned = Incident.Appliances.Where(a => a.SectorId is null).ToList();
        if (unassigned.Count > 0) groups.Add(new ApplianceGroup("Unassigned", unassigned));

        return groups;
    }

    // Recomputed wholesale on every poll, same as ApplianceGroups -- no
    // independent scroll state to lose the way Timeline has.
    public List<IncidentActionDto> Actions => Incident?.Actions ?? [];

    // Overview/Attendance, Tasks & Requests, and Timeline used to all be
    // visible cards stacked in a fixed-height column -- once there were
    // three of them the page genuinely didn't fit and Timeline got
    // squeezed to a sliver. One tab visible at a time, each getting the
    // full remaining height, is the actual fix for that, not another
    // ScrollView patch.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewSelected))]
    [NotifyPropertyChangedFor(nameof(IsTasksSelected))]
    [NotifyPropertyChangedFor(nameof(IsTimelineSelected))]
    private string selectedTab = "Overview";

    public bool IsOverviewSelected => SelectedTab == "Overview";
    public bool IsTasksSelected => SelectedTab == "Tasks";
    public bool IsTimelineSelected => SelectedTab == "Timeline";

    [RelayCommand]
    private void SelectTab(string tab) => SelectedTab = tab;

    // Counts of "would you want to know this without switching tabs" --
    // not a generic item count. Recomputed wholesale alongside Actions/
    // Timeline in RefreshAsync.
    public int OpenActionsCount => Actions.Count(a => a.Status == "Open");
    public int UnacknowledgedUpdateCount => Timeline.Count(e => e.Acknowledgeable && e.AcknowledgedAtUtc is null);

    [ObservableProperty]
    private string actionKind = "Task";

    [ObservableProperty]
    private string actionText = string.Empty;

    [ObservableProperty]
    private bool isRaisingAction;

    [ObservableProperty]
    private bool isAddingAction;

    // Which Timeline entry, if any, the shared note box's next Post
    // targets as a reply -- null means Post sends a plain new note.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReplying))]
    [NotifyPropertyChangedFor(nameof(ReplyingToText))]
    private string? replyingToId;

    public bool IsReplying => ReplyingToId is not null;

    // What the reply banner quotes -- looked up from the current
    // Timeline rather than stored separately, so it can never drift from
    // what SyncTimeline actually has.
    public string? ReplyingToText => Timeline.FirstOrDefault(e => e.Id == ReplyingToId)?.Text;

    // Reflects only whether the last poll actually succeeded -- not a
    // real-time connection state (this app polls, it doesn't hold a live
    // socket). Good enough to tell a crew "the tablet hasn't heard back
    // in a while", which is the actual thing worth surfacing without
    // taking on SignalR/a persistent connection.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectivityLabel))]
    private bool isOnline = true;

    public string ConnectivityLabel => IsOnline ? "Live" : "Reconnecting...";

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
        // Text lookup by update id, so a reply can quote what it's
        // replying to -- same "replies stay in the flat list with a
        // quoted preview" approach the web console uses.
        var textById = incident.Updates.ToDictionary(u => u.Id, u => u.Text);

        var entries = new List<TimelineEntry>
        {
            new("created", "Created", $"Incident created: {incident.IncidentType}", incident.OrgUnitName, incident.StartedAtUtc,
                Acknowledgeable: false, AcknowledgedAtUtc: null, AcknowledgedByName: null, CanReply: false, ReplyToText: null),
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
            u.CreatedAtUtc,
            Acknowledgeable: u.UpdateType is "General" or "Hazard",
            u.AcknowledgedAtUtc,
            u.AcknowledgedByName,
            CanReply: true,
            ReplyToText: u.ReplyToUpdateId is { } replyId ? textById.GetValueOrDefault(replyId, "a since-removed message") : null)));
        if (incident.ClosedAtUtc is { } closedAt)
            entries.Add(new("closed", "Closed", "Incident closed", incident.Status, closedAt,
                Acknowledgeable: false, AcknowledgedAtUtc: null, AcknowledgedByName: null, CanReply: false, ReplyToText: null));

        return entries.OrderBy(e => e.Timestamp).ToList();
    }

    // Mostly append-only -- an IncidentUpdate row's text/author/timestamp is
    // immutable once written -- but AcknowledgedAtUtc can flip on an entry
    // already in the list (a crew member acks a note minutes after it
    // posted), so an existing entry whose acknowledgement changed gets
    // replaced in place rather than skipped, same index, same scroll
    // position preserved either way.
    private void SyncTimeline(List<TimelineEntry> latest)
    {
        foreach (var entry in latest)
        {
            var index = -1;
            for (var i = 0; i < Timeline.Count; i++)
            {
                if (Timeline[i].Id != entry.Id) continue;
                index = i;
                break;
            }

            if (index < 0) Timeline.Add(entry);
            else if (Timeline[index].AcknowledgedAtUtc != entry.AcknowledgedAtUtc) Timeline[index] = entry;
        }
        OnPropertyChanged(nameof(UnacknowledgedUpdateCount));
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
                IsOnline = false;
                return;
            }

            ErrorMessage = string.Empty;
            IsOnline = true;
            // Only re-notify MapSource when the coordinates actually moved --
            // this fires on every 15s poll, and reassigning WebView.Source
            // unconditionally would reload the map (losing the crew's pan/
            // zoom) even when nothing about the location changed.
            var locationChanged = Incident?.Latitude != result.Latitude || Incident?.Longitude != result.Longitude;
            Incident = result;
            OnPropertyChanged(nameof(FormattedAddress));
            OnPropertyChanged(nameof(IncidentNumberLabel));
            OnPropertyChanged(nameof(StartedLabel));
            OnPropertyChanged(nameof(ApplianceGroups));
            OnPropertyChanged(nameof(Actions));
            OnPropertyChanged(nameof(OpenActionsCount));
            SyncTimeline(BuildTimeline(result));
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
            IsOnline = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Same input box serves both "add a note" and "reply to X" -- when
    // ReplyingToId is set (via StartReplyCommand on a specific Timeline
    // entry), Post sends this same text as a reply instead of a fresh
    // note, then clears the reply state. One box, one Post button, no
    // per-row inline forms needing a value comparison MAUI's simple
    // DataTrigger binding can't express.
    [RelayCommand]
    private async Task AddNoteAsync()
    {
        if (string.IsNullOrWhiteSpace(NoteText) || IsPostingNote) return;

        IsPostingNote = true;
        try
        {
            Guid? replyToId = ReplyingToId is { } replying && Guid.TryParse(replying, out var parsed) ? parsed : null;
            var (result, error) = await apiClient.AddNoteAsync(IncidentId, NoteText.Trim(), replyToId);

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
            SyncTimeline(BuildTimeline(result));
            NoteText = string.Empty;
            ReplyingToId = null;
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
    private async Task AcknowledgeUpdateAsync(string updateId)
    {
        if (!Guid.TryParse(updateId, out var id)) return;
        try
        {
            var (result, error) = await apiClient.AcknowledgeUpdateAsync(IncidentId, id);
            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is not null)
            {
                Incident = result;
                SyncTimeline(BuildTimeline(result));
            }
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
    }

    // Sets which entry the shared note box's next Post will target -- see
    // AddNoteAsync's own comment for why there's one input box, not a
    // per-row inline form.
    [RelayCommand]
    private void StartReply(string updateId) => ReplyingToId = updateId;

    [RelayCommand]
    private void CancelReply() => ReplyingToId = null;

    [RelayCommand]
    private void ToggleAddingAction() => IsAddingAction = !IsAddingAction;

    [RelayCommand]
    private void SetActionKind(string kind) => ActionKind = kind;

    [RelayCommand]
    private async Task RaiseActionAsync()
    {
        if (string.IsNullOrWhiteSpace(ActionText) || IsRaisingAction) return;

        IsRaisingAction = true;
        try
        {
            var request = new AddActionRequest(ActionKind, ActionText.Trim());
            var (result, error) = await apiClient.AddActionAsync(IncidentId, request);

            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is null)
            {
                ErrorMessage = error ?? "Couldn't raise that. Try again.";
                return;
            }

            ErrorMessage = string.Empty;
            Incident = result;
            OnPropertyChanged(nameof(Actions));
            OnPropertyChanged(nameof(OpenActionsCount));
            SyncTimeline(BuildTimeline(result));
            ActionText = string.Empty;
            IsAddingAction = false;
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong raising that.";
        }
        finally
        {
            IsRaisingAction = false;
        }
    }

    [RelayCommand]
    private async Task AcknowledgeActionAsync(Guid actionId)
    {
        try
        {
            var (result, error) = await apiClient.AcknowledgeActionAsync(IncidentId, actionId);
            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is not null)
            {
                Incident = result;
                OnPropertyChanged(nameof(Actions));
                OnPropertyChanged(nameof(OpenActionsCount));
                SyncTimeline(BuildTimeline(result));
            }
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
    }

    [RelayCommand]
    private async Task CompleteActionAsync(Guid actionId) => await ResolveActionAsync(actionId, "Completed");

    [RelayCommand]
    private async Task DeclineActionAsync(Guid actionId) => await ResolveActionAsync(actionId, "Declined");

    private async Task ResolveActionAsync(Guid actionId, string status)
    {
        try
        {
            var (result, error) = await apiClient.ResolveActionAsync(IncidentId, actionId, status);
            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is not null)
            {
                Incident = result;
                OnPropertyChanged(nameof(Actions));
                OnPropertyChanged(nameof(OpenActionsCount));
                SyncTimeline(BuildTimeline(result));
            }
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
    }

    [RelayCommand]
    private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");

    [RelayCommand]
    private async Task ViewHierarchyAsync() => await Shell.Current.GoToAsync($"incident-hierarchy?id={IncidentId}");

    // Navigates immediately rather than awaiting the attendance-update call
    // first -- that call is already best-effort/a no-op API-side (no
    // Callsign set), so blocking the transition to the driving screen on it
    // too just adds a second network round-trip of dead time on top of
    // NavigateViewModel's own (GPS fix, report, route). Fired instead, its
    // own failure is now visible (see UpdateAttendanceStatusAsync below),
    // just not until the crew is back looking at this page.
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
                SyncTimeline(BuildTimeline(result));
            }
            // Not surfaced live -- the crew is already on NavigatePage by
            // the time this resolves. But this failing silently (Sentry
            // only) meant Control Room could be shown as still at the
            // station with genuinely no way for the crew to know. Setting
            // it here means the pinned error banner on THIS page shows it
            // the next time they're back here, instead of it vanishing
            // entirely.
            else if (error is not null && error != ApiClient.RevokedError)
            {
                ErrorMessage = "Couldn't update attendance status when navigation started -- check Control Room knows you're en route.";
            }
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Couldn't update attendance status when navigation started -- check Control Room knows you're en route.";
        }
    }

    public void Dispose()
    {
        if (refreshTimer is not null) refreshTimer.Stop();
        GC.SuppressFinalize(this);
    }
}
