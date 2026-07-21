using System.ComponentModel;
using MusterHubCommandTablet.ViewModels;

namespace MusterHubCommandTablet.Views;

public partial class IncidentDetailPage : ContentPage
{
    private readonly IncidentDetailViewModel viewModel;

    // Tracks whether the bundled Leaflet page has finished loading -- it
    // reloads (wiping its JS state) whenever MapSource changes, so
    // setRoute/clearRoute can only be evaluated again once Navigated fires.
    private bool mapLoaded;

    public IncidentDetailPage(IncidentDetailViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
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
        _ = MapWebView.EvaluateJavaScriptAsync(viewModel.RouteScript);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IncidentDetailViewModel.MapSource)) mapLoaded = false;
        if (mapLoaded && e.PropertyName == nameof(IncidentDetailViewModel.RouteScript))
            _ = MapWebView.EvaluateJavaScriptAsync(viewModel.RouteScript);
    }
}
