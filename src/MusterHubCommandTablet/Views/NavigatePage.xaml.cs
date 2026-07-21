using System.ComponentModel;
using MusterHubCommandTablet.ViewModels;

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
        if (e.PropertyName == nameof(NavigateViewModel.MapSource)) mapLoaded = false;
        if (mapLoaded && e.PropertyName == nameof(NavigateViewModel.RouteScript))
            _ = MapWebView.EvaluateJavaScriptAsync(viewModel.RouteScript);
    }
}
