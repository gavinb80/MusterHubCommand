using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusterHubCommandTablet.Models;
using MusterHubCommandTablet.Services;
using Sentry;

namespace MusterHubCommandTablet.ViewModels;

[QueryProperty(nameof(IncidentId), "id")]
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

    [ObservableProperty]
    private IncidentDto? incident;

    [ObservableProperty]
    private string noteText = string.Empty;

    [ObservableProperty]
    private bool isPostingNote;

    public bool HasLocation => Incident?.Latitude is not null && Incident?.Longitude is not null;

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
