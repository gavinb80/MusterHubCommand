namespace MusterHubCommand.Api.Contracts;

public record OrganisationSettingsDto(double GeofenceRadiusMeters, bool BaEntryControlEnabled);

public record UpdateOrganisationSettingsRequest(double GeofenceRadiusMeters, bool BaEntryControlEnabled);
