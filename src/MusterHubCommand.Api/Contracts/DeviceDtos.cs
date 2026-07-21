namespace MusterHubCommand.Api.Contracts;

public record DeviceDto(
    Guid Id, string Label, Guid OrgUnitId, string OrgUnitName,
    Guid? VehicleProfileId, string? VehicleProfileName, string? Callsign,
    double? CurrentLatitude, double? CurrentLongitude, DateTimeOffset? LocationUpdatedAtUtc,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? LastSeenAtUtc, bool IsActive);

public record CreateDeviceRequest(string Label, Guid OrgUnitId);

public record UpdateDeviceLocationRequest(double Latitude, double Longitude);

// The plaintext token is returned exactly once, at creation -- same
// "show once, never again" convention as every other secret-issuance flow
// in the MusterHub codebases (core's own API keys, Rota/Skills' integration
// keys). Nothing after this call can ever retrieve it again; losing it
// means re-pairing the tablet with a freshly generated one.
public record CreateDeviceResponse(Guid Id, string Label, string PairingToken);
