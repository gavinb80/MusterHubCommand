namespace MusterHubCommand.Api.Contracts;

public record OrganisationSettingsDto(double GeofenceRadiusMeters);

public record UpdateOrganisationSettingsRequest(double GeofenceRadiusMeters);
