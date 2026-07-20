using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using MusterHubCommandTablet.Services;
using MusterHubCommandTablet.ViewModels;
using MusterHubCommandTablet.Views;

namespace MusterHubCommandTablet;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit();

		if (!string.IsNullOrWhiteSpace(AppConfig.SentryDsn))
		{
			builder.UseSentry(options =>
			{
				options.Dsn = AppConfig.SentryDsn;
				options.Debug = false;
				options.TracesSampleRate = 0;
				options.IsGlobalModeEnabled = true;
			});
		}

		builder.Services.AddSingleton(new HttpClient());
		builder.Services.AddSingleton<IDeviceTokenStore, DeviceTokenStore>();
		builder.Services.AddSingleton<IApiClient, ApiClient>();

		builder.Services.AddSingleton<AppShell>();
		builder.Services.AddTransient<PairingViewModel>();
		builder.Services.AddTransient<PairingPage>();
		builder.Services.AddTransient<ActiveIncidentsViewModel>();
		builder.Services.AddTransient<ActiveIncidentsPage>();
		builder.Services.AddTransient<IncidentDetailViewModel>();
		builder.Services.AddTransient<IncidentDetailPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
