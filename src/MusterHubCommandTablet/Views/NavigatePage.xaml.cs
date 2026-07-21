using System.ComponentModel;
using MusterHubCommandTablet.ViewModels;
using Sentry;

namespace MusterHubCommandTablet.Views;

public partial class NavigatePage : ContentPage
{
    private readonly NavigateViewModel viewModel;
    private bool mapLoaded;

    public NavigatePage(NavigateViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.ScriptRequested += script => _ = RunScriptAsync(script);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.OnDisappearing();
    }

    private void OnMapNavigated(object? sender, WebNavigatedEventArgs e)
    {
        mapLoaded = true;
        _ = RunScriptAsync(viewModel.RouteScript);
        _ = RunScriptAsync(viewModel.HeadingScript);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigateViewModel.MapSource)) mapLoaded = false;
        if (!mapLoaded) return;

        if (e.PropertyName == nameof(NavigateViewModel.RouteScript))
            _ = RunScriptAsync(viewModel.RouteScript);
        if (e.PropertyName == nameof(NavigateViewModel.HeadingScript))
            _ = RunScriptAsync(viewModel.HeadingScript);
    }

    // Awaited and caught rather than a bare fire-and-forget "_ = ..." --
    // an unobserved exception from a discarded Task vanishes silently,
    // which is exactly how a real bug here went undiagnosed earlier.
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
