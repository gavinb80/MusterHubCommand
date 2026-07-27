using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusterHubCommandTablet.Models;
using MusterHubCommandTablet.Services;
using Sentry;

namespace MusterHubCommandTablet.ViewModels;

// Wraps the plain DTO with a lazily-populated Thumbnail -- a bare
// ImageSource pointed at the API URL can't carry the device's Bearer
// token, so the bytes are fetched through IApiClient.DownloadAttachmentAsync
// and wrapped in ImageSource.FromStream instead (mirrors the web app's
// apiFetchBlob + object-URL approach).
public partial class AttachmentItem : ObservableObject
{
    public AttachmentItem(IncidentAttachmentDto dto)
    {
        Dto = dto;
    }

    public IncidentAttachmentDto Dto { get; }
    public bool IsImage => Dto.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    // Shown in the thumbnail square in place of an image for a document
    // (e.g. a PDF) -- there's nothing to preview, just what kind of file it is.
    public string FileExtensionLabel => Dto.FileName.Contains('.') ? Dto.FileName[(Dto.FileName.LastIndexOf('.') + 1)..].ToUpperInvariant() : "";

    [ObservableProperty]
    private ImageSource? thumbnail;
}

[QueryProperty(nameof(IncidentIdString), "id")]
public partial class PhotoCaptureViewModel : BaseViewModel
{
    private readonly IApiClient apiClient;

    public PhotoCaptureViewModel(IApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    private Guid incidentId;

    // Same Shell QueryProperty workaround IncidentHierarchyViewModel's own
    // IncidentIdString uses -- Shell hands query values across as strings.
    public string IncidentIdString
    {
        get => incidentId.ToString();
        set
        {
            if (Guid.TryParse(value, out var parsed)) incidentId = parsed;
        }
    }

    public ObservableCollection<AttachmentItem> Attachments { get; } = [];

    public bool HasNoAttachments => !IsBusy && Attachments.Count == 0 && !HasError;

    [ObservableProperty]
    private bool isUploading;

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
                ErrorMessage = error ?? "Couldn't load this incident's photos.";
                return;
            }

            ErrorMessage = string.Empty;
            Attachments.Clear();
            foreach (var attachment in result.Attachments) AddAttachment(attachment);
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong loading this incident's photos.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasNoAttachments));
        }
    }

    private void AddAttachment(IncidentAttachmentDto dto, bool insertAtStart = false)
    {
        var item = new AttachmentItem(dto);
        if (insertAtStart) Attachments.Insert(0, item);
        else Attachments.Add(item);

        if (item.IsImage) _ = LoadThumbnailAsync(item);
    }

    // Fire-and-forget per item, not awaited by the caller -- a slow or
    // failed thumbnail fetch for one photo shouldn't hold up the rest of
    // the list rendering or block the appearing/upload flow.
    private async Task LoadThumbnailAsync(AttachmentItem item)
    {
        try
        {
            var (stream, _) = await apiClient.DownloadAttachmentAsync(item.Dto.Id);
            if (stream is null) return;

            // The factory re-seeks every time CollectionView asks for the
            // image (recycled cells, orientation changes) -- the same
            // MemoryStream is reused rather than re-fetched from the API.
            item.Thumbnail = ImageSource.FromStream(() =>
            {
                stream.Position = 0;
                return stream;
            });
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
    }

    [RelayCommand]
    private async Task TakePhotoAsync()
    {
        if (!MediaPicker.Default.IsCaptureSupported)
        {
            ErrorMessage = "This device can't take photos.";
            return;
        }

        var status = await Permissions.RequestAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
        {
            ErrorMessage = "Camera permission is needed to attach a photo.";
            return;
        }

        try
        {
            var photo = await MediaPicker.Default.CapturePhotoAsync();
            if (photo is null) return;

            await UploadAsync(photo);
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong capturing that photo.";
        }
    }

    private async Task UploadAsync(FileResult photo)
    {
        IsUploading = true;
        try
        {
            await using var stream = await photo.OpenReadAsync();
            var contentType = photo.ContentType is { Length: > 0 } ct ? ct : "image/jpeg";
            var (result, error) = await apiClient.UploadAttachmentAsync(incidentId, stream, photo.FileName, contentType);

            if (error == ApiClient.RevokedError)
            {
                await Shell.Current.GoToAsync("//pairing");
                return;
            }

            if (result is null)
            {
                ErrorMessage = error ?? "Couldn't upload that photo.";
                return;
            }

            ErrorMessage = string.Empty;
            AddAttachment(result, insertAtStart: true);
            OnPropertyChanged(nameof(HasNoAttachments));
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong uploading that photo.";
        }
        finally
        {
            IsUploading = false;
        }
    }

    [RelayCommand]
    private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");

    [RelayCommand]
    private async Task ViewPhotoAsync(AttachmentItem item)
    {
        if (!item.IsImage) return;
        await Shell.Current.GoToAsync($"photo-viewer?id={item.Dto.Id}&fileName={Uri.EscapeDataString(item.Dto.FileName)}");
    }
}
