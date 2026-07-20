using MusterHubCommandTablet.ViewModels;

namespace MusterHubCommandTablet.Views;

public partial class IncidentDetailPage : ContentPage
{
    private readonly IncidentDetailViewModel viewModel;

    public IncidentDetailPage(IncidentDetailViewModel viewModel)
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

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.OnDisappearing();
    }
}
