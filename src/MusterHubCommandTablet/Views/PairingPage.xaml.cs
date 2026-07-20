using MusterHubCommandTablet.ViewModels;

namespace MusterHubCommandTablet.Views;

public partial class PairingPage : ContentPage
{
    public PairingPage(PairingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
