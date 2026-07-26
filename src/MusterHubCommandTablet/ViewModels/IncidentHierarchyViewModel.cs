using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusterHubCommandTablet.Models;
using MusterHubCommandTablet.Services;
using MusterHubCommandTablet.Views;
using Sentry;

namespace MusterHubCommandTablet.ViewModels;

[QueryProperty(nameof(IncidentIdString), "id")]
public partial class IncidentHierarchyViewModel : BaseViewModel
{
    private readonly IApiClient apiClient;

    public IncidentHierarchyViewModel(IApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    private Guid incidentId;
    private IncidentDto? lastIncident;

    // View-only state, not server state -- default expanded, keyed by
    // node id so it survives a manual toggle-driven rebuild without
    // needing to round-trip to the API.
    private readonly Dictionary<Guid, bool> expandedState = [];

    // Same Shell QueryProperty workaround IncidentDetailViewModel's own
    // IncidentIdString uses -- Shell hands query values across as strings,
    // and Convert.ChangeType has no string->Guid conversion.
    public string IncidentIdString
    {
        get => incidentId.ToString();
        set
        {
            if (Guid.TryParse(value, out var parsed)) incidentId = parsed;
        }
    }

    public ObservableCollection<PositionedNode> Nodes { get; } = [];

    // Flattened across every node so a single BindableLayout can position
    // every appliance box independently in the same canvas coordinate
    // space its owning node's box lives in -- nesting a second
    // BindableLayout inside each node's own template would position
    // appliances relative to that node's box instead.
    public ObservableCollection<PositionedAppliance> Appliances { get; } = [];

    [ObservableProperty]
    private HierarchyEdgesDrawable edgesDrawable = new([], Colors.Transparent);

    [ObservableProperty]
    private double canvasWidth = 1;

    [ObservableProperty]
    private double canvasHeight = 1;

    public bool HasNoHierarchy => !IsBusy && Nodes.Count == 0 && !HasError;

    public async Task OnAppearingAsync()
    {
        if (incidentId == Guid.Empty) return;
        IsBusy = true;
        try
        {
            var (result, error) = await apiClient.GetIncidentAsync(incidentId);

            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }

            if (result is null)
            {
                ErrorMessage = error ?? "Couldn't load this incident's hierarchy.";
                return;
            }

            ErrorMessage = string.Empty;
            lastIncident = result;
            RebuildLayout();
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong loading this incident's hierarchy.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasNoHierarchy));
        }
    }

    private void RebuildLayout()
    {
        if (lastIncident is null) return;

        var result = HierarchyLayout.Build(lastIncident, expandedState);
        Nodes.Clear();
        Appliances.Clear();
        foreach (var node in result.Nodes)
        {
            Nodes.Add(node);
            foreach (var appliance in node.Appliances) Appliances.Add(appliance);
        }
        CanvasWidth = result.CanvasWidth;
        CanvasHeight = result.CanvasHeight;

        // A fresh Drawable instance (not a mutated one) so reassigning
        // this bindable property is itself what triggers GraphicsView to
        // redraw -- no manual Invalidate() plumbing from code-behind.
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var borderColor = (Color)Application.Current!.Resources[isDark ? "BorderDark" : "BorderLight"];
        EdgesDrawable = new HierarchyEdgesDrawable(result.Edges, borderColor);
    }

    [RelayCommand]
    private void ToggleExpand(Guid nodeId)
    {
        expandedState[nodeId] = !expandedState.GetValueOrDefault(nodeId, true);
        RebuildLayout();
    }

    [RelayCommand]
    private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
}
