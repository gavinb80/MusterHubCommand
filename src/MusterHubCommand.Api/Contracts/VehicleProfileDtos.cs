namespace MusterHubCommand.Api.Contracts;

public record VehicleProfileDto(Guid Id, string Name, double? MaxWeightTonnes, double? MaxHeightMetres, double? MaxWidthMetres);

public record SaveVehicleProfileRequest(string Name, double? MaxWeightTonnes, double? MaxHeightMetres, double? MaxWidthMetres);
