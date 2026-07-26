using MusterHubCommandTablet.Services;
using MusterHubCommandTablet.Views;

namespace MusterHubCommandTablet;

public partial class AppShell : Shell
{
    public AppShell(IDeviceTokenStore tokenStore, LocationReportingService locationService)
    {
        InitializeComponent();

        // Pushed from the home screen with an incident id, not a root tab --
        // registered here rather than as a ShellContent since it only ever
        // makes sense on top of the incidents list, never navigated to
        // directly.
        Routing.RegisterRoute("incident-detail", typeof(IncidentDetailPage));
        Routing.RegisterRoute("incident-hierarchy", typeof(IncidentHierarchyPage));
        Routing.RegisterRoute("navigate", typeof(NavigatePage));

        // Shell always lands on its first ShellContent ("pairing") by
        // default -- an already-paired tablet restarting (power cycle,
        // app update) needs to skip straight past it instead of asking
        // whoever's nearby to re-enter a code that was already used once.
        Loaded += async (_, _) =>
        {
            if (await tokenStore.GetAsync() is not null)
            {
                locationService.Start();
                await GoToAsync("//home");
            }
        };
    }
}
