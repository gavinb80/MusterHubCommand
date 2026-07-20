using MusterHubCommandTablet.ViewModels;

namespace MusterHubCommandTablet.Views;

public partial class ActiveIncidentsPage : ContentPage
{
    private readonly ActiveIncidentsViewModel viewModel;

    public ActiveIncidentsPage(ActiveIncidentsViewModel viewModel)
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
