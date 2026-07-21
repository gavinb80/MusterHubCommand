namespace MusterHubCommand.Api.Contracts;

public record GeocodeResponseDto(bool Found, double? Latitude, double? Longitude, string? DisplayName);
