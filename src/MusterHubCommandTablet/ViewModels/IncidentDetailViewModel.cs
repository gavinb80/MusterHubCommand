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

// BA Entry Control's Teams/Wearers section, flattened into one list the
// same way TimelineEntry already flattens Updates -- a CollectionView with
// a BindableLayout nested inside each of its own item templates (Teams
// outer, Wearers inner) measures wrong on this MAUI version: the inner
// list's own height doesn't reflow into its CollectionView cell once a
// wearer's added, leaving a huge blank cell with the actual row rendered
// off in a corner of it. One flat, single-level list sidesteps that class
// of bug entirely rather than trying to outguess it again. Kind picks which
// DataTemplate BaDisplayRowTemplateSelector hands out for a given row --
// a *selector*, not one shared template with IsVisible toggles, because
// IsVisible-hidden siblings inside a recycled CollectionView cell were
// still leaving their measured space behind (the grey dead-space bug).
// Team/Wearer carry whichever DTO is relevant to that Kind, null otherwise.
// CountdownDisplay/Urgency are recomputed against ServerNow (this device's
// clock, corrected against the server's -- see ServerNow's own comment)
// every time BaTeamRows rebuilds, which the ViewModel's own countdownTimer
// forces once a second while the BA tab is open -- see StartCountdownTimer.
public record BaDisplayRow(string Kind, BaTeamDto? Team = null, BaWearerDto? Wearer = null)
{
    // Wearer rows only: "12:34" counting down, or "-03:21" once overdue.
    public string? CountdownDisplay { get; init; }

    // Wearer rows: "Normal" | "Warning" (<=5 min left) | "Overdue" | "Exited".
    // TeamHeader rows: "InBa" | "Warning" (someone's due out inside 5 min) |
    // "Overdue" | "Exited" (all wearers out) | "Empty".
    public string Urgency { get; init; } = "Normal";

    // TeamHeader rows only -- "3 in BA" / "Overdue" / "All out". Reuses
    // Urgency as the colour key so it goes through the same
    // StatusToColorConverter every other status pill in this app does,
    // not a bespoke palette just for this one pill. CountdownDisplay on a
    // TeamHeader row is that team's own earliest whistle time counting
    // down (the soonest of its non-exited wearers), not any one wearer's.
    public string? TeamStatusText { get; init; }

    // TeamHeader rows only -- collapsed teams still show this header row
    // (name, status, countdown, +Wearer), just none of their AddWearerForm/
    // NoWearers/Wearer rows below it. Defaults true so a newly-created team
    // starts open rather than needing a tap to reveal itself.
    public bool IsExpanded { get; init; } = true;
}

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
    private IDispatcherTimer? countdownTimer;

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

    // Captured from every response's own IncidentDto.ServerNowUtc, not
    // read once -- keeps correcting itself if this device's clock is
    // adjusted (or just drifts) mid-session, rather than freezing whatever
    // offset happened to be true at pairing time.
    private TimeSpan serverClockOffset;

    partial void OnIncidentChanged(IncidentDto? value)
    {
        if (value is not null) serverClockOffset = value.ServerNowUtc - DateTimeOffset.UtcNow;
    }

    // BA Entry Control's own "now" -- this device's clock corrected by the
    // gap to the server's, not trusted outright. See IncidentDto.
    // ServerNowUtc's own comment for why (Android emulators in particular
    // can show a plausible time while the date's a day off).
    private DateTimeOffset ServerNow => DateTimeOffset.UtcNow + serverClockOffset;

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
    public List<IncidentObjectiveDto> Objectives => Incident?.Objectives ?? [];
    public List<IncidentRiskDto> Risks => Incident?.Risks ?? [];
    public List<BaEntryControlPointDto> BaEntryControlPoints => Incident?.BaEntryControlPoints ?? [];

    // A device owns at most one point at a time (see
    // TabletIncidentsController.AddBaEntryControlPoint/Claim), so the tab
    // never actually needs to render a *list* of points -- just this
    // device's own one, if it has one, plus whatever else is still
    // unclaimed for it to pick up.
    public BaEntryControlPointDto? OwnedBaPoint => BaEntryControlPoints.FirstOrDefault(p => p.IsOwnedByThisDevice);
    public List<BaEntryControlPointDto> UnclaimedBaPoints => BaEntryControlPoints.Where(p => !p.IsOwnedByThisDevice).ToList();
    public bool HasOwnedBaPoint => OwnedBaPoint is not null;
    public bool HasUnclaimedBaPoints => UnclaimedBaPoints.Count > 0;

    // The point's Teams and each team's Wearers, flattened into one list --
    // see BaDisplayRow's own comment for why. Recomputed from scratch
    // whenever the point data, which team's add-wearer form is open, or
    // (once a second, while the BA tab is open) the clock changes -- all
    // three notify this via NotifyBaPointsChanged/the countdown timer.
    public List<BaDisplayRow> BaTeamRows
    {
        get
        {
            if (OwnedBaPoint is not { } point) return [];
            var rows = new List<BaDisplayRow>();
            foreach (var team in point.Teams)
            {
                var expanded = !collapsedBaTeamIds.Contains(team.Id);
                rows.Add(new BaDisplayRow("TeamHeader", Team: team)
                {
                    Urgency = TeamUrgency(team),
                    TeamStatusText = TeamStatusText(team),
                    CountdownDisplay = TeamEarliestWhistleCountdown(team),
                    IsExpanded = expanded,
                });
                if (!expanded) continue;

                if (AddingWearerForTeamId == team.Id) rows.Add(new BaDisplayRow("AddWearerForm", Team: team));
                if (team.Wearers.Count == 0) rows.Add(new BaDisplayRow("NoWearers", Team: team));
                foreach (var wearer in team.Wearers)
                {
                    var remaining = wearer.WhistleAtUtc - ServerNow;
                    rows.Add(new BaDisplayRow("Wearer", Team: team, Wearer: wearer)
                    {
                        CountdownDisplay = wearer.Status == "Exited" ? $"Out {wearer.ExitedAtUtc:t}" : FormatCountdown(remaining),
                        Urgency = wearer.Status switch
                        {
                            "Exited" => "Exited",
                            "Overdue" => "Overdue",
                            _ when remaining <= TimeSpan.FromMinutes(5) => "Warning",
                            _ => "Normal",
                        },
                    });
                }
            }
            return rows;
        }
    }

    // Absence means expanded -- a newly-created team should never need a
    // tap just to see the wearer it was created to hold.
    private readonly HashSet<Guid> collapsedBaTeamIds = [];

    [RelayCommand]
    private void ToggleBaTeamExpanded(Guid teamId)
    {
        if (!collapsedBaTeamIds.Remove(teamId)) collapsedBaTeamIds.Add(teamId);
        OnPropertyChanged(nameof(BaTeamRows));
    }

    private string TeamUrgency(BaTeamDto team)
    {
        if (team.Wearers.Count == 0) return "Empty";
        if (team.Wearers.Any(w => w.Status == "Overdue")) return "Overdue";
        var soonestInBa = team.Wearers.Where(w => w.Status == "InBa")
            .Select(w => w.WhistleAtUtc - ServerNow)
            .DefaultIfEmpty(TimeSpan.MaxValue).Min();
        if (soonestInBa <= TimeSpan.FromMinutes(5)) return "Warning";
        return team.Wearers.Any(w => w.Status == "InBa") ? "InBa" : "Exited";
    }

    private string? TeamStatusText(BaTeamDto team) => TeamUrgency(team) switch
    {
        "Empty" => null,
        "Overdue" => "Overdue",
        "Exited" => "All out",
        _ => $"{team.Wearers.Count(w => w.Status != "Exited")} in BA",
    };

    // The team-level echo of the request: a live countdown to whoever in
    // this team is due out soonest, not any one wearer's own timer -- null
    // once every wearer's exited, so the header just falls back to the
    // "All out" pill above with nothing counting down beside it.
    private string? TeamEarliestWhistleCountdown(BaTeamDto team)
    {
        var soonest = team.Wearers.Where(w => w.Status != "Exited").OrderBy(w => w.WhistleAtUtc).FirstOrDefault();
        return soonest is null ? null : FormatCountdown(soonest.WhistleAtUtc - ServerNow);
    }

    // "12:34" counting down, "-03:21" once past the whistle time -- always
    // MM:SS, never a raw negative TimeSpan, since an ECO reads this as a
    // countdown clock, not arithmetic.
    private static string FormatCountdown(TimeSpan remaining)
    {
        var overdue = remaining < TimeSpan.Zero;
        var magnitude = overdue ? -remaining : remaining;
        var text = $"{(int)magnitude.TotalMinutes:00}:{magnitude.Seconds:00}";
        return overdue ? $"-{text}" : text;
    }

    // Dashboard strip on the point header: how many are actually in BA
    // right now, how many of those are overdue, and who's due out
    // soonest -- the "do I need to act before I even open a team" glance.
    public int OverdueBaCount => OwnedBaPoint?.Teams.SelectMany(t => t.Wearers).Count(w => w.Status == "Overdue") ?? 0;

    public string? NextDueOutDisplay
    {
        get
        {
            var next = OwnedBaPoint?.Teams.SelectMany(t => t.Wearers)
                .Where(w => w.Status != "Exited")
                .OrderBy(w => w.WhistleAtUtc)
                .FirstOrDefault();
            if (next is null) return null;
            var remaining = FormatCountdown(next.WhistleAtUtc - ServerNow);
            return next.Status == "Overdue" ? $"{next.Name} overdue by {remaining[1..]}" : $"{next.Name} due out in {remaining}";
        }
    }

    // Overview/Attendance, Objectives, Tasks & Requests, Timeline, Risk Log
    // and BA Entry Control used to all be visible cards stacked in a
    // fixed-height column -- once there were three of them the page
    // genuinely didn't fit and Timeline got squeezed to a sliver. One tab
    // visible at a time, each getting the full remaining height, is the
    // actual fix for that, not another ScrollView patch.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewSelected))]
    [NotifyPropertyChangedFor(nameof(IsObjectivesSelected))]
    [NotifyPropertyChangedFor(nameof(IsTasksSelected))]
    [NotifyPropertyChangedFor(nameof(IsTimelineSelected))]
    [NotifyPropertyChangedFor(nameof(IsRisksSelected))]
    [NotifyPropertyChangedFor(nameof(IsBaSelected))]
    private string selectedTab = "Overview";

    public bool IsOverviewSelected => SelectedTab == "Overview";
    public bool IsObjectivesSelected => SelectedTab == "Objectives";
    public bool IsTasksSelected => SelectedTab == "Tasks";
    public bool IsTimelineSelected => SelectedTab == "Timeline";
    public bool IsRisksSelected => SelectedTab == "Risks";
    public bool IsBaSelected => SelectedTab == "Ba";

    [RelayCommand]
    private void SelectTab(string tab)
    {
        SelectedTab = tab;
        if (IsBaSelected) StartCountdownTimer(); else StopCountdownTimer();
    }

    // A second, faster timer than refreshTimer's own 15s poll -- this one
    // makes no network call, it just forces BaTeamRows (and the dashboard
    // counts) to recompute against the current clock every second, so an
    // ECO sees a live MM:SS countdown rather than a static timestamp they
    // have to do the maths on themselves. Only ticks while the BA tab is
    // actually open, not for the page's whole lifetime.
    //
    // BaTeamRows returns a brand-new List<BaDisplayRow> on every call, so
    // notifying it every second makes the CollectionView treat its whole
    // ItemsSource as replaced, not just updated -- it was regenerating
    // every cell each tick, which yanked focus straight back out of
    // whatever Entry the ECO had just tapped into (name/pressure/whistle
    // while adding a wearer). Skipping the BaTeamRows notify while a form
    // is actually open fixes that; the countdown just holds still for the
    // few seconds the form's up rather than fighting the keyboard for
    // focus, and catches up the instant it closes.
    private void StartCountdownTimer()
    {
        if (countdownTimer is not null) return;
        countdownTimer = Application.Current!.Dispatcher.CreateTimer();
        countdownTimer.Interval = TimeSpan.FromSeconds(1);
        countdownTimer.Tick += (_, _) =>
        {
            if (AddingWearerForTeamId is null && !IsAddingBaTeam && !IsAddingBaPoint)
                OnPropertyChanged(nameof(BaTeamRows));
            OnPropertyChanged(nameof(OverdueBaCount));
            OnPropertyChanged(nameof(NextDueOutDisplay));
        };
        countdownTimer.Start();
    }

    private void StopCountdownTimer()
    {
        countdownTimer?.Stop();
        countdownTimer = null;
    }

    // Counts of "would you want to know this without switching tabs" --
    // not a generic item count. Recomputed wholesale alongside Actions/
    // Objectives/Risks/BaEntryControlPoints/Timeline in RefreshAsync.
    public int OpenActionsCount => Actions.Count(a => a.Status == "Open");
    public int OpenObjectivesCount => Objectives.Count(o => o.Status == "Open");
    public int IdentifiedRisksCount => Risks.Count(r => r.Status == "Identified");
    public int InBaCount => OwnedBaPoint?.Teams.SelectMany(t => t.Wearers).Count(w => w.Status != "Exited") ?? 0;
    public int UnacknowledgedUpdateCount => Timeline.Count(e => e.Acknowledgeable && e.AcknowledgedAtUtc is null);

    [ObservableProperty]
    private string actionKind = "Task";

    [ObservableProperty]
    private string actionText = string.Empty;

    [ObservableProperty]
    private bool isRaisingAction;

    [ObservableProperty]
    private bool isAddingAction;

    [ObservableProperty]
    private string objectiveText = string.Empty;

    [ObservableProperty]
    private bool isRaisingObjective;

    [ObservableProperty]
    private bool isAddingObjective;

    [ObservableProperty]
    private string riskDescription = string.Empty;

    [ObservableProperty]
    private string riskLevel = "Medium";

    [ObservableProperty]
    private string riskControlMeasure = string.Empty;

    [ObservableProperty]
    private bool isRaisingRisk;

    [ObservableProperty]
    private bool isAddingRisk;

    // BA Entry Control's own add-form state (Point -> Team -> Wearer). A
    // device only ever has one point of its own, so "is the team form
    // open" is a plain bool -- only AddingWearerForTeamId still needs to
    // track *which* item's form is open, since a point can genuinely run
    // several teams at once.
    [ObservableProperty]
    private bool isAddingBaPoint;

    [ObservableProperty]
    private string baPointName = string.Empty;

    [ObservableProperty]
    private string baPointStage = "II";

    [ObservableProperty]
    private bool isRaisingBaPoint;

    [ObservableProperty]
    private bool isAddingBaTeam;

    [ObservableProperty]
    private string baTeamName = string.Empty;

    [ObservableProperty]
    private string baTeamLeader = string.Empty;

    [ObservableProperty]
    private string baTeamCommsChannel = string.Empty;

    [ObservableProperty]
    private string baTeamBriefing = string.Empty;

    [ObservableProperty]
    private string baTeamEquipment = string.Empty;

    [ObservableProperty]
    private bool isRaisingBaTeam;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BaTeamRows))]
    private Guid? addingWearerForTeamId;

    [ObservableProperty]
    private string baWearerName = string.Empty;

    [ObservableProperty]
    private string baWearerCylinderPressureBar = "232";

    // Minutes, not a clock time -- an ECO thinks in "how long until this
    // cylinder's due out", not clock arithmetic; converted to the absolute
    // WhistleAtUtc timestamp the API actually stores right before sending,
    // same UX call the web console's own (read-only) BaBoardPanel display
    // assumes too.
    [ObservableProperty]
    private string baWearerWhistleMinutes = "25";

    [ObservableProperty]
    private bool isRaisingBaWearer;

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

    // Same fetch-once-not-every-poll shape as GeofenceRadiusMeters -- gates
    // whether the BA tab renders at all for this org, see IsBaSelected's
    // own tab-strip XAML.
    [ObservableProperty]
    private bool baEntryControlEnabled;

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
        // Shell navigation to a sibling screen and back re-enters this same
        // VM instance rather than a fresh one, so OnAppearingAsync can run
        // more than once per instance -- unsubscribe first so repeat visits
        // don't stack up duplicate Tick handlers on the same timer.
        refreshTimer.Tick -= OnRefreshTimerTick;
        refreshTimer.Tick += OnRefreshTimerTick;
        refreshTimer.Start();
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e) => await RefreshAsync();

    private async Task LoadGeofenceRadiusAsync()
    {
        try
        {
            var (result, error) = await apiClient.GetOrganisationSettingsAsync();
            if (error != ApiClient.RevokedError && result is not null)
            {
                GeofenceRadiusMeters = result.GeofenceRadiusMeters;
                BaEntryControlEnabled = result.BaEntryControlEnabled;
            }
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
    }

    public void OnDisappearing()
    {
        if (refreshTimer is not null) refreshTimer.Tick -= OnRefreshTimerTick;
        refreshTimer?.Stop();
        StopCountdownTimer();
    }

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
            OnPropertyChanged(nameof(Objectives));
            OnPropertyChanged(nameof(OpenObjectivesCount));
            OnPropertyChanged(nameof(Risks));
            OnPropertyChanged(nameof(IdentifiedRisksCount));
            // Same reasoning as the countdown timer's own guard: rebuilding
            // BaTeamRows mid-poll would regenerate the CollectionView cell
            // an ECO's actively typing into and drop focus out from under
            // them. The underlying Incident is still updated either way --
            // once the form closes, the next notify (or the next poll)
            // catches everything up.
            if (AddingWearerForTeamId is null && !IsAddingBaTeam && !IsAddingBaPoint) NotifyBaPointsChanged();
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
    private void ToggleAddingObjective() => IsAddingObjective = !IsAddingObjective;

    // No SyncTimeline call anywhere in these three -- unlike every Action
    // command above, an objective's status change never lands in the
    // Timeline, so there's nothing there to re-sync.
    [RelayCommand]
    private async Task RaiseObjectiveAsync()
    {
        if (string.IsNullOrWhiteSpace(ObjectiveText) || IsRaisingObjective) return;

        IsRaisingObjective = true;
        try
        {
            var (result, error) = await apiClient.AddObjectiveAsync(IncidentId, ObjectiveText.Trim());

            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is null)
            {
                ErrorMessage = error ?? "Couldn't add that objective. Try again.";
                return;
            }

            ErrorMessage = string.Empty;
            Incident = result;
            OnPropertyChanged(nameof(Objectives));
            OnPropertyChanged(nameof(OpenObjectivesCount));
            ObjectiveText = string.Empty;
            IsAddingObjective = false;
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong adding that objective.";
        }
        finally
        {
            IsRaisingObjective = false;
        }
    }

    [RelayCommand]
    private async Task AchieveObjectiveAsync(Guid objectiveId)
    {
        try
        {
            var (result, error) = await apiClient.AchieveObjectiveAsync(IncidentId, objectiveId);
            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is not null)
            {
                Incident = result;
                OnPropertyChanged(nameof(Objectives));
                OnPropertyChanged(nameof(OpenObjectivesCount));
            }
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
    }

    [RelayCommand]
    private async Task ReopenObjectiveAsync(Guid objectiveId)
    {
        try
        {
            var (result, error) = await apiClient.ReopenObjectiveAsync(IncidentId, objectiveId);
            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is not null)
            {
                Incident = result;
                OnPropertyChanged(nameof(Objectives));
                OnPropertyChanged(nameof(OpenObjectivesCount));
            }
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
    }

    [RelayCommand]
    private void ToggleAddingRisk() => IsAddingRisk = !IsAddingRisk;

    [RelayCommand]
    private void SetRiskLevel(string level) => RiskLevel = level;

    // Unlike RaiseObjectiveAsync, this DOES call SyncTimeline -- raising a
    // risk lands a Hazard entry server-side (see the API's own
    // IncidentService.RaiseRiskAsync), so there's something new to reflect.
    // ControlRiskAsync/ReopenRiskAsync below don't, same reasoning
    // Objectives' own Achieve/Reopen already established.
    [RelayCommand]
    private async Task RaiseRiskAsync()
    {
        if (string.IsNullOrWhiteSpace(RiskDescription) || IsRaisingRisk) return;

        IsRaisingRisk = true;
        try
        {
            var (result, error) = await apiClient.AddRiskAsync(
                IncidentId, RiskDescription.Trim(), RiskLevel,
                string.IsNullOrWhiteSpace(RiskControlMeasure) ? null : RiskControlMeasure.Trim());

            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is null)
            {
                ErrorMessage = error ?? "Couldn't add that risk. Try again.";
                return;
            }

            ErrorMessage = string.Empty;
            Incident = result;
            OnPropertyChanged(nameof(Risks));
            OnPropertyChanged(nameof(IdentifiedRisksCount));
            SyncTimeline(BuildTimeline(result));
            RiskDescription = string.Empty;
            RiskLevel = "Medium";
            RiskControlMeasure = string.Empty;
            IsAddingRisk = false;
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong adding that risk.";
        }
        finally
        {
            IsRaisingRisk = false;
        }
    }

    [RelayCommand]
    private async Task ControlRiskAsync(Guid riskId)
    {
        try
        {
            var (result, error) = await apiClient.ControlRiskAsync(IncidentId, riskId);
            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is not null)
            {
                Incident = result;
                OnPropertyChanged(nameof(Risks));
                OnPropertyChanged(nameof(IdentifiedRisksCount));
            }
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
    }

    [RelayCommand]
    private async Task ReopenRiskAsync(Guid riskId)
    {
        try
        {
            var (result, error) = await apiClient.ReopenRiskAsync(IncidentId, riskId);
            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is not null)
            {
                Incident = result;
                OnPropertyChanged(nameof(Risks));
                OnPropertyChanged(nameof(IdentifiedRisksCount));
            }
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
    }

    [RelayCommand]
    private void ToggleAddingBaPoint() => IsAddingBaPoint = !IsAddingBaPoint;

    [RelayCommand]
    private void SetBaPointStage(string stage) => BaPointStage = stage;

    private void NotifyBaPointsChanged()
    {
        OnPropertyChanged(nameof(BaEntryControlPoints));
        OnPropertyChanged(nameof(OwnedBaPoint));
        OnPropertyChanged(nameof(UnclaimedBaPoints));
        OnPropertyChanged(nameof(HasOwnedBaPoint));
        OnPropertyChanged(nameof(HasUnclaimedBaPoints));
        OnPropertyChanged(nameof(InBaCount));
        OnPropertyChanged(nameof(OverdueBaCount));
        OnPropertyChanged(nameof(NextDueOutDisplay));
        OnPropertyChanged(nameof(BaTeamRows));
    }

    // No SyncTimeline call anywhere in this whole Point/Team/Wearer group --
    // BA Entry Control never touches the Timeline at all (see the API's
    // own BaEntryControlPoint comment).
    [RelayCommand]
    private async Task RaiseBaPointAsync()
    {
        if (string.IsNullOrWhiteSpace(BaPointName) || IsRaisingBaPoint) return;

        IsRaisingBaPoint = true;
        try
        {
            var (result, error) = await apiClient.AddBaEntryControlPointAsync(IncidentId, BaPointName.Trim(), BaPointStage);

            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is null)
            {
                ErrorMessage = error ?? "Couldn't add that entry control point. Try again.";
                return;
            }

            ErrorMessage = string.Empty;
            Incident = result;
            NotifyBaPointsChanged();
            BaPointName = string.Empty;
            BaPointStage = "II";
            IsAddingBaPoint = false;
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong adding that entry control point.";
        }
        finally
        {
            IsRaisingBaPoint = false;
        }
    }

    [RelayCommand]
    private async Task ClaimBaPointAsync(Guid pointId)
    {
        try
        {
            var (result, error) = await apiClient.ClaimBaEntryControlPointAsync(IncidentId, pointId);
            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is null)
            {
                ErrorMessage = error ?? "Couldn't claim that entry control point. Try again.";
                return;
            }
            ErrorMessage = string.Empty;
            Incident = result;
            NotifyBaPointsChanged();
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong claiming that entry control point.";
        }
    }

    [RelayCommand]
    private async Task HandOverBaPointAsync(Guid pointId)
    {
        try
        {
            var (result, error) = await apiClient.HandOverBaEntryControlPointAsync(IncidentId, pointId);
            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is not null)
            {
                Incident = result;
                NotifyBaPointsChanged();
            }
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
    }

    [RelayCommand]
    private void ToggleAddingBaTeam()
    {
        IsAddingBaTeam = !IsAddingBaTeam;
        BaTeamName = string.Empty;
        BaTeamLeader = string.Empty;
        BaTeamCommsChannel = string.Empty;
        BaTeamBriefing = string.Empty;
        BaTeamEquipment = string.Empty;
    }

    [RelayCommand]
    private async Task RaiseBaTeamAsync()
    {
        if (string.IsNullOrWhiteSpace(BaTeamName) || string.IsNullOrWhiteSpace(BaTeamLeader) || IsRaisingBaTeam) return;
        if (OwnedBaPoint is not { } point) return;

        IsRaisingBaTeam = true;
        try
        {
            var (result, error) = await apiClient.AddBaTeamAsync(
                IncidentId, point.Id, BaTeamName.Trim(), BaTeamLeader.Trim(),
                string.IsNullOrWhiteSpace(BaTeamCommsChannel) ? null : BaTeamCommsChannel.Trim(),
                string.IsNullOrWhiteSpace(BaTeamBriefing) ? null : BaTeamBriefing.Trim(),
                string.IsNullOrWhiteSpace(BaTeamEquipment) ? null : BaTeamEquipment.Trim());

            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is null)
            {
                ErrorMessage = error ?? "Couldn't add that team. Try again.";
                return;
            }

            ErrorMessage = string.Empty;
            Incident = result;
            NotifyBaPointsChanged();
            IsAddingBaTeam = false;
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong adding that team.";
        }
        finally
        {
            IsRaisingBaTeam = false;
        }
    }

    [RelayCommand]
    private void ToggleAddingBaWearer(Guid teamId)
    {
        AddingWearerForTeamId = AddingWearerForTeamId == teamId ? null : teamId;
        BaWearerName = string.Empty;
        BaWearerCylinderPressureBar = "232";
        BaWearerWhistleMinutes = "25";
        // Opening the form on a collapsed team should actually show it,
        // not silently do nothing because its rows aren't in BaTeamRows.
        if (AddingWearerForTeamId == teamId) collapsedBaTeamIds.Remove(teamId);
    }

    [RelayCommand]
    private async Task RaiseBaWearerAsync(Guid teamId)
    {
        if (string.IsNullOrWhiteSpace(BaWearerName) || IsRaisingBaWearer) return;
        if (!double.TryParse(BaWearerCylinderPressureBar, out var pressure) || !int.TryParse(BaWearerWhistleMinutes, out var minutes)) return;
        if (OwnedBaPoint is not { } point) return;

        IsRaisingBaWearer = true;
        try
        {
            // minutes, not a DateTimeOffset this device computed -- see
            // AddBaWearerRequest's own comment for why: the server
            // resolves the actual WhistleAtUtc against its own clock.
            var (result, error) = await apiClient.AddBaWearerAsync(
                IncidentId, point.Id, teamId, BaWearerName.Trim(), pressure, minutes);

            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is null)
            {
                ErrorMessage = error ?? "Couldn't add that wearer. Try again.";
                return;
            }

            ErrorMessage = string.Empty;
            Incident = result;
            NotifyBaPointsChanged();
            AddingWearerForTeamId = null;
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong adding that wearer.";
        }
        finally
        {
            IsRaisingBaWearer = false;
        }
    }

    [RelayCommand]
    private async Task ExitBaWearerAsync(Guid wearerId)
    {
        if (OwnedBaPoint is not { } point) return;
        var team = point.Teams.FirstOrDefault(t => t.Wearers.Any(w => w.Id == wearerId));
        if (team is null) return;

        try
        {
            var (result, error) = await apiClient.ExitBaWearerAsync(IncidentId, point.Id, team.Id, wearerId);
            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is not null)
            {
                Incident = result;
                NotifyBaPointsChanged();
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

    [RelayCommand]
    private async Task ViewPhotosAsync() => await Shell.Current.GoToAsync($"incident-photos?id={IncidentId}");

    [RelayCommand]
    private async Task AnnotateMapAsync() => await Shell.Current.GoToAsync($"annotate-map?id={IncidentId}");

    // Leaflet's own zoomControl is disabled (see index.html's comment) --
    // same plain-event pattern NavigateViewModel.ScriptRequested already
    // establishes, since the View owns the WebView reference, not this
    // ViewModel, and a command needs to run every tap even when two taps
    // in a row produce the identical script string.
    public event Action<string>? ScriptRequested;

    [RelayCommand]
    private void ZoomIn() => ScriptRequested?.Invoke("map.zoomIn();");

    [RelayCommand]
    private void ZoomOut() => ScriptRequested?.Invoke("map.zoomOut();");

    [RelayCommand]
    private void RecenterMap() => ScriptRequested?.Invoke("centerOnIncident();");

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
        if (refreshTimer is not null)
        {
            refreshTimer.Tick -= OnRefreshTimerTick;
            refreshTimer.Stop();
        }
        StopCountdownTimer();
        GC.SuppressFinalize(this);
    }
}
