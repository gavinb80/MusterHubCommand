namespace MusterHubCommand.Api.Contracts;

public record IntegrationApiKeyDto(Guid Id, string Label, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastUsedAtUtc, bool IsActive);

public record CreateIntegrationApiKeyRequest(string Label);

// Plaintext shown exactly once, at creation -- same convention as
// CreateDeviceResponse.
public record CreateIntegrationApiKeyResponse(Guid Id, string Label, string ApiKey);
