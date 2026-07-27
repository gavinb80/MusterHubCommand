using MusterHubCommandTablet.ViewModels;

namespace MusterHubCommandTablet.Views;

public partial class PhotoCapturePage : ContentPage
{
    private readonly PhotoCaptureViewModel viewModel;

    public PhotoCapturePage(PhotoCaptureViewModel viewModel)
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
}
