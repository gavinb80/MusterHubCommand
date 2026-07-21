using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusterHubCommandTablet.Services;
using Sentry;

namespace MusterHubCommandTablet.ViewModels;

// First-run (and post-revocation) screen: an Operator reads the pairing
// code shown once in Setup > Devices out to whoever's setting up this
// tablet. No QR/camera flow for V1 -- a kiosk tablet mounted on an
// appliance is set up once, rarely, by someone standing next to it with
// the code already in hand.
public partial class PairingViewModel(IApiClient apiClient, LocationReportingService locationService) : BaseViewModel
{
    [ObservableProperty]
    private string pairingCode = string.Empty;

    [RelayCommand]
    private async Task PairAsync()
    {
        if (string.IsNullOrWhiteSpace(PairingCode) || IsBusy) return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var (ok, error) = await apiClient.TryPairAsync(PairingCode.Trim());
            if (!ok)
            {
                ErrorMessage = error ?? "Pairing failed.";
                return;
            }

            locationService.Start();
            await Shell.Current.GoToAsync("//home");
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            ErrorMessage = "Something went wrong pairing this tablet. Try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
