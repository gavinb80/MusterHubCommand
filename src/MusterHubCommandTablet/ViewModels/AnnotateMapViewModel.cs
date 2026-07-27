using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusterHubCommandTablet.Services;
using Sentry;

namespace MusterHubCommandTablet.ViewModels;

[QueryProperty(nameof(IncidentIdString), "id")]
public partial class AnnotateMapViewModel : BaseViewModel
{
    private readonly IApiClient apiClient;

    public AnnotateMapViewModel(IApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    private Guid incidentId;

    // Same Shell QueryProperty workaround every other detail-ish page uses.
    public string IncidentIdString
    {
        get => incidentId.ToString();
        set
        {
            if (Guid.TryParse(value, out var parsed)) incidentId = parsed;
        }
    }

    [ObservableProperty]
    private double? latitude;

    [ObservableProperty]
    private double? longitude;

    [ObservableProperty]
    private double? geofenceRadiusMeters;

    // The bundled Leaflet page never draws a route line unless setRoute/
    // setNavRoute is explicitly evaluated -- this page never does that, so
    // the map is route-free by construction, no suppression logic needed.
    public UrlWebViewSource? MapSource => Latitude is null || Longitude is null
        ? null
        : new UrlWebViewSource
        {
            Url = $"map/index.html?lat={Latitude.Value.ToString(CultureInfo.InvariantCulture)}&lng={Longitude.Value.ToString(CultureInfo.InvariantCulture)}" +
                  (GeofenceRadiusMeters is { } r ? $"&radius={r.ToString(CultureInfo.InvariantCulture)}" : ""),
        };

    [ObservableProperty]
    private bool isAnnotating;

    [ObservableProperty]
    private ImageSource? capturedImage;

    private static readonly Color[] PenColors =
    [
        Color.FromArgb("#FF453A"), Color.FromArgb("#FFD60A"), Color.FromArgb("#0A84FF"), Color.FromArgb("#111111"),
    ];

    [ObservableProperty]
    private Color selectedColor = PenColors[0];

    // float, not double -- DrawingView.LineWidth is a float, and compiled
    // bindings need the exact type to match.
    [ObservableProperty]
    private float selectedWidth = 4;

    public ObservableCollection<IDrawingLine> Lines { get; } = [];

    // The page owns the WebView/Grid the ViewModel needs to capture -- same
    // "ViewModel raises a plain event, code-behind does the native call"
    // split NavigateViewModel.ScriptRequested already establishes for
    // driving a WebView from a ViewModel that doesn't hold a reference to it.
    public event Action? CaptureRequested;
    public event Action? SaveRequested;

    // Same script-string event as CaptureRequested/SaveRequested above --
    // Leaflet's own zoomControl is disabled (see index.html's comment), so
    // the map stage needs its own +/- and recenter, matching
    // IncidentDetailViewModel's identical controls on the small inline map.
    public event Action<string>? ScriptRequested;

    [RelayCommand]
    private void ZoomIn() => ScriptRequested?.Invoke("map.zoomIn();");

    [RelayCommand]
    private void ZoomOut() => ScriptRequested?.Invoke("map.zoomOut();");

    [RelayCommand]
    private void RecenterMap() => ScriptRequested?.Invoke("centerOnIncident();");

    public async Task OnAppearingAsync()
    {
        if (incidentId == Guid.Empty) return;
        IsBusy = true;
        try
        {
            var (incident, incidentError) = await apiClient.GetIncidentAsync(incidentId);
            if (incidentError == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (incident is null)
            {
                ErrorMessage = incidentError ?? "Couldn't load this incident's location.";
                return;
            }

            var (settings, settingsError) = await apiClient.GetOrganisationSettingsAsync();
            if (settingsError != ApiClient.RevokedError && settings is not null) GeofenceRadiusMeters = settings.GeofenceRadiusMeters;

            ErrorMessage = string.Empty;
            Latitude = incident.Latitude;
            Longitude = incident.Longitude;
            OnPropertyChanged(nameof(MapSource));
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong loading the map.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Capture() => CaptureRequested?.Invoke();

    // Called back by the page once it has actually captured the WebView.
    public void OnMapCaptured(byte[] png)
    {
        CapturedImage = ImageSource.FromStream(() => new MemoryStream(png));
        Lines.Clear();
        IsAnnotating = true;
    }

    [RelayCommand]
    private void SelectColor(string hex) => SelectedColor = Color.FromArgb(hex);

    // Takes a string, not a double -- a XAML CommandParameter literal like
    // "9" is passed through as a boxed string, and RelayCommand<double>
    // would throw an InvalidCastException trying to unbox it directly.
    [RelayCommand]
    private void SelectWidth(string width) => SelectedWidth = float.Parse(width, CultureInfo.InvariantCulture);

    [RelayCommand]
    private void Undo()
    {
        if (Lines.Count > 0) Lines.RemoveAt(Lines.Count - 1);
    }

    [RelayCommand]
    private void Clear() => Lines.Clear();

    [RelayCommand]
    private void BackToMap()
    {
        IsAnnotating = false;
        CapturedImage = null;
        Lines.Clear();
    }

    [RelayCommand]
    private void Save() => SaveRequested?.Invoke();

    // Called back by the page once it has captured the annotated composite.
    public async Task UploadCompositeAsync(byte[] png)
    {
        IsBusy = true;
        try
        {
            var stream = new MemoryStream(png);
            var fileName = $"map-annotation-{DateTime.UtcNow:yyyyMMddHHmmss}.png";
            var (result, error) = await apiClient.UploadAttachmentAsync(incidentId, stream, fileName, "image/png");

            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }
            if (result is null)
            {
                ErrorMessage = error ?? "Couldn't save that to the incident.";
                return;
            }

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong saving that to the incident.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
}
