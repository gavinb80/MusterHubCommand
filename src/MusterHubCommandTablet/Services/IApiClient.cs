using MusterHubCommandTablet.Models;

namespace MusterHubCommandTablet.Services;

public interface IApiClient
{
    // Validates a pairing code by using it directly as a bearer token
    // against a Device-scheme endpoint -- MusterHubCommand.Api has no
    // separate "exchange" step, the code an Operator is shown in Setup
    // already IS the long-lived device token (see DevicesController.Create).
    Task<(bool Ok, string? Error)> TryPairAsync(string pairingCode);

    Task<(List<IncidentSummaryDto>? Result, string? Error)> GetActiveIncidentsAsync();
    Task<(IncidentDto? Result, string? Error)> GetIncidentAsync(Guid id);
    Task<(IncidentDto? Result, string? Error)> AddNoteAsync(Guid incidentId, string text);
    Task<(RouteResponseDto? Result, string? Error)> GetRouteAsync(Guid incidentId);
    Task<string?> ReportLocationAsync(double latitude, double longitude);
}
