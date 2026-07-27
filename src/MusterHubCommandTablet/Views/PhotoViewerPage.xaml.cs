using MusterHubCommandTablet.ViewModels;

namespace MusterHubCommandTablet.Views;

public partial class PhotoViewerPage : ContentPage
{
    private readonly PhotoViewerViewModel viewModel;
    private double currentScale = 1;

    public PhotoViewerPage(PhotoViewerViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = viewModel.OnAppearingAsync();
    }

    // Pinch-to-zoom only (no pan) -- enough to inspect detail in a photo
    // without building a full gesture-tracking image viewer. Clamped so a
    // stray pinch can't shrink the image to nothing or blow it up
    // off-screen.
    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Running)
        {
            currentScale = Math.Clamp(currentScale * e.Scale, 1, 5);
            PhotoImage.Scale = currentScale;
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        currentScale = 1;
        PhotoImage.Scale = 1;
    }
}
