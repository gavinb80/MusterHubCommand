using MusterHubCommandTablet.Services;
using MusterHubCommandTablet.ViewModels;
using Sentry;

namespace MusterHubCommandTablet.Views;

public partial class AnnotateMapPage : ContentPage
{
    private readonly AnnotateMapViewModel viewModel;

    public AnnotateMapPage(AnnotateMapViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
        viewModel.CaptureRequested += () => _ = CaptureMapAsync();
        viewModel.SaveRequested += () => _ = SaveCompositeAsync();
        viewModel.ScriptRequested += script => _ = RunScriptAsync(script);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = viewModel.OnAppearingAsync();
    }

    private void OnMapNavigated(object? sender, WebNavigatedEventArgs e)
    {
        // Nothing to push into the page on load -- unlike IncidentDetailPage/
        // NavigatePage, this map never calls setRoute, so there's no JS to
        // run once it's ready.
    }

    private async Task CaptureMapAsync()
    {
        try
        {
            var png = await NativeViewCapture.CaptureAsync(MapWebView);
            viewModel.OnMapCaptured(png);
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            viewModel.ErrorMessage = "Couldn't capture the map view.";
        }
    }

    private async Task SaveCompositeAsync()
    {
        try
        {
            var png = await NativeViewCapture.CaptureAsync(AnnotationGrid);
            await viewModel.UploadCompositeAsync(png);
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            viewModel.ErrorMessage = "Couldn't save the annotated map.";
        }
    }

    private async Task RunScriptAsync(string script)
    {
        try
        {
            await MapWebView.EvaluateJavaScriptAsync(script);
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
    }
}
