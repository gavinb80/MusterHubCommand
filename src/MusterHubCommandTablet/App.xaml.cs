using Sentry;

namespace MusterHubCommandTablet;

public partial class App : Application
{
	private readonly AppShell appShell;

	public App(AppShell appShell)
	{
		InitializeComponent();
		this.appShell = appShell;

		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
		{
			if (e.ExceptionObject is Exception ex) SentrySdk.CaptureException(ex);
		};

		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			SentrySdk.CaptureException(e.Exception);
			e.SetObserved();
		};
	}

	protected override Window CreateWindow(IActivationState? activationState) => new(appShell);
}
