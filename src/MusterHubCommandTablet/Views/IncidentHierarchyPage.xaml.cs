using MusterHubCommandTablet.ViewModels;

namespace MusterHubCommandTablet.Views;

public partial class IncidentHierarchyPage : ContentPage
{
    private readonly IncidentHierarchyViewModel viewModel;

    public IncidentHierarchyPage(IncidentHierarchyViewModel viewModel)
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
