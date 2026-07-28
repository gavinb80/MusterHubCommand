namespace MusterHubCommand.Api.Data.Entities;

// One row per org, created lazily on first PUT to api/organisation-settings
// -- a GET before that just returns the default.
public class OrganisationSettings : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    // Metres from an incident's own Latitude/Longitude within which an
    // appliance's reported GPS auto-marks it OnScene -- see GeofenceService.
    public double GeofenceRadiusMeters { get; set; } = 50;

    // Not every service runs BA boards the same way (some, e.g. water
    // rescue units, wouldn't want it cluttering their incident view), so
    // this is opt-in per org rather than always-on -- gates both the
    // BaEntry endpoints (see IncidentsController) and whether the tab even
    // renders client-side.
    public bool BaEntryControlEnabled { get; set; }
}
