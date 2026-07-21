namespace MusterHubCommand.Api.Routing;

// Unset (dev without a routerdb built, or a fresh prod deploy before
// Gavin's uploaded one) means RoutingService.IsAvailable is false and every
// route request returns a clear "not available" response rather than
// crashing -- the rest of the incident detail screen works fine without it.
public class RoutingOptions
{
    public const string SectionName = "Routing";
    public string? RouterDbPath { get; set; }
}
