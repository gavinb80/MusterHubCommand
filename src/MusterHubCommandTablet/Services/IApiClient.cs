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
    Task<(IncidentDto? Result, string? Error)> AddNoteAsync(Guid incidentId, string text, Guid? replyToUpdateId = null);
    Task<(IncidentDto? Result, string? Error)> AcknowledgeUpdateAsync(Guid incidentId, Guid updateId);
    Task<(IncidentDto? Result, string? Error)> AddActionAsync(Guid incidentId, AddActionRequest request);
    Task<(IncidentDto? Result, string? Error)> AcknowledgeActionAsync(Guid incidentId, Guid actionId);
    Task<(IncidentDto? Result, string? Error)> ResolveActionAsync(Guid incidentId, Guid actionId, string status);
    Task<(IncidentDto? Result, string? Error)> AddObjectiveAsync(Guid incidentId, string text);
    Task<(IncidentDto? Result, string? Error)> AchieveObjectiveAsync(Guid incidentId, Guid objectiveId);
    Task<(IncidentDto? Result, string? Error)> ReopenObjectiveAsync(Guid incidentId, Guid objectiveId);
    Task<(RouteResponseDto? Result, string? Error)> GetRouteAsync(Guid incidentId);
    Task<string?> ReportLocationAsync(double latitude, double longitude);
    Task<(IncidentDto? Result, string? Error)> StartNavigationAsync(Guid incidentId);
    Task<(OrganisationSettingsDto? Result, string? Error)> GetOrganisationSettingsAsync();
    Task<(TabletDeviceDto? Result, string? Error)> GetDeviceAsync();
    Task<(IncidentAttachmentDto? Result, string? Error)> UploadAttachmentAsync(Guid incidentId, Stream content, string fileName, string contentType);

    // A plain ImageSource can't carry the device's Bearer token, so
    // rendering a thumbnail (or opening a full preview) fetches the bytes
    // through here first -- same reasoning as the web app's apiFetchBlob.
    Task<(Stream? Result, string? Error)> DownloadAttachmentAsync(Guid attachmentId);
}
