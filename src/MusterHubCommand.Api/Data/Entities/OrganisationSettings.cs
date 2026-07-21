namespace MusterHubCommand.Api.Data.Entities;

// One row per org, created lazily on first PUT to api/organisation-settings
// -- a GET before that just returns the default. Small on purpose: the one
// field here today (the arrival geofence radius) is unlikely to be the
// last org-wide setting this module needs, but nothing beyond that is
// speculatively added ahead of an actual second use.
public class OrganisationSettings : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    // Metres from an incident's own Latitude/Longitude within which an
    // appliance's reported GPS auto-marks it OnScene -- see GeofenceService.
    public double GeofenceRadiusMeters { get; set; } = 50;
}
