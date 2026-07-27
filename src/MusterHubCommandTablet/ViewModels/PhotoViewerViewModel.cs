using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusterHubCommandTablet.Services;
using Sentry;

namespace MusterHubCommandTablet.ViewModels;

// Fetches its own copy of the attachment rather than reusing the
// thumbnail's already-loaded ImageSource -- the thumbnail may not have
// finished loading yet (or may have failed) by the time this page opens,
// and Shell's query-string navigation can't carry a live ImageSource
// across pages anyway, only serialisable values like the id.
[QueryProperty(nameof(AttachmentIdString), "id")]
[QueryProperty(nameof(FileName), "fileName")]
public partial class PhotoViewerViewModel : BaseViewModel
{
    private readonly IApiClient apiClient;

    public PhotoViewerViewModel(IApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    private Guid attachmentId;

    public string AttachmentIdString
    {
        get => attachmentId.ToString();
        set
        {
            if (Guid.TryParse(value, out var parsed)) attachmentId = parsed;
        }
    }

    [ObservableProperty]
    private string fileName = string.Empty;

    [ObservableProperty]
    private ImageSource? image;

    public async Task OnAppearingAsync()
    {
        if (attachmentId == Guid.Empty) return;
        IsBusy = true;
        try
        {
            var (stream, error) = await apiClient.DownloadAttachmentAsync(attachmentId);

            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }

            if (stream is null)
            {
                ErrorMessage = error ?? "Couldn't load this photo.";
                return;
            }

            ErrorMessage = string.Empty;
            Image = ImageSource.FromStream(() =>
            {
                stream.Position = 0;
                return stream;
            });
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong loading this photo.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
}
