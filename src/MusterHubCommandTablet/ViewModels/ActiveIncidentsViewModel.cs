using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusterHubCommandTablet.Models;
using MusterHubCommandTablet.Services;
using Sentry;

namespace MusterHubCommandTablet.ViewModels;

// Home screen: this station's active incidents, refreshed on a plain
// polling loop rather than push -- a kiosk-mounted, always-on device has
// no backgrounding problem to solve for, and this is the one screen a
// crew glances at without touching anything, so it has to update itself.
public partial class ActiveIncidentsViewModel : BaseViewModel, IDisposable
{
    private readonly IApiClient apiClient;
    private readonly IDeviceTokenStore tokenStore;
    private IDispatcherTimer? refreshTimer;

    public ActiveIncidentsViewModel(IApiClient apiClient, IDeviceTokenStore tokenStore)
    {
        this.apiClient = apiClient;
        this.tokenStore = tokenStore;
    }

    public ObservableCollection<IncidentSummaryDto> Incidents { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoIncidents))]
    private bool loadedOnce;

    public bool HasNoIncidents => LoadedOnce && Incidents.Count == 0;

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
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var (result, error) = await apiClient.GetActiveIncidentsAsync();

            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }

            if (result is null)
            {
                ErrorMessage = error ?? "Couldn't load incidents.";
                return;
            }

            ErrorMessage = string.Empty;
            SyncIncidents(result);
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong loading incidents.";
        }
        finally
        {
            IsBusy = false;
            LoadedOnce = true;
        }
    }

    // Updates the existing ObservableCollection in place rather than
    // clearing and re-adding -- a full clear/re-add on every 15s poll would
    // flash the list and drop scroll position on a screen someone might be
    // actively reading.
    private void SyncIncidents(List<IncidentSummaryDto> latest)
    {
        for (var i = Incidents.Count - 1; i >= 0; i--)
        {
            if (latest.All(l => l.Id != Incidents[i].Id)) Incidents.RemoveAt(i);
        }

        foreach (var incident in latest)
        {
            var existingIndex = Incidents.ToList().FindIndex(i => i.Id == incident.Id);
            if (existingIndex < 0) Incidents.Add(incident);
            else if (Incidents[existingIndex].UpdatedAtUtc != incident.UpdatedAtUtc) Incidents[existingIndex] = incident;
        }
    }

    [RelayCommand]
    private async Task OpenIncidentAsync(IncidentSummaryDto? incident)
    {
        if (incident is null) return;
        await Shell.Current.GoToAsync($"incident-detail?id={incident.Id}");
    }

    // Confirmed, not immediate: this is a shared, kiosk-mounted device --
    // an accidental tap here strands the appliance without its incident
    // feed until someone re-enters a pairing code from Setup.
    [RelayCommand]
    private async Task UnpairAsync()
    {
        var confirmed = await Shell.Current.CurrentPage.DisplayAlertAsync(
            "Unpair this tablet?", "It will stop showing incidents until it's paired again with a new code from Setup.", "Unpair", "Cancel");
        if (!confirmed) return;

        refreshTimer?.Stop();
        await tokenStore.ClearAsync();
        await Shell.Current.GoToAsync("//pairing");
    }

    public void Dispose()
    {
        if (refreshTimer is not null) refreshTimer.Stop();
        GC.SuppressFinalize(this);
    }
}
