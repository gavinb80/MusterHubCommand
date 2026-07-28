using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MusterHubCommandTablet.Models;

namespace MusterHubCommandTablet.Services;

public class ApiClient(HttpClient httpClient, IDeviceTokenStore tokenStore) : IApiClient
{
    // Sentinel error string ViewModels check for -- a 401 here always means
    // an Operator revoked this device from Setup (a device token, once
    // paired, is otherwise long-lived and never expires on its own), so
    // every caller reacts to it the same way: clear storage, bounce to
    // the pairing screen.
    public const string RevokedError = "revoked";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<(bool Ok, string? Error)> TryPairAsync(string pairingCode)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{AppConfig.ApiBaseUrl}/api/tablet/incidents");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pairingCode);
            using var response = await httpClient.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return (false, "That pairing code isn't valid, or has already been used. Ask an Operator to check Setup > Devices.");

            if (!response.IsSuccessStatusCode)
                return (false, $"Muster Hub Command returned an error ({(int)response.StatusCode}).");

            await tokenStore.SaveAsync(pairingCode);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach Muster Hub Command ({ex.Message}).");
        }
    }

    public Task<(List<IncidentSummaryDto>? Result, string? Error)> GetActiveIncidentsAsync() =>
        GetAsync<List<IncidentSummaryDto>>("api/tablet/incidents");

    public Task<(IncidentDto? Result, string? Error)> GetIncidentAsync(Guid id) =>
        GetAsync<IncidentDto>($"api/tablet/incidents/{id}");

    public Task<(IncidentDto? Result, string? Error)> AddNoteAsync(Guid incidentId, string text, Guid? replyToUpdateId = null) =>
        PostAsync<AddCrewNoteRequest, IncidentDto>($"api/tablet/incidents/{incidentId}/notes", new AddCrewNoteRequest(text, null, replyToUpdateId));

    public Task<(IncidentDto? Result, string? Error)> AcknowledgeUpdateAsync(Guid incidentId, Guid updateId) =>
        PostNoBodyAsync<IncidentDto>($"api/tablet/incidents/{incidentId}/updates/{updateId}/acknowledge");

    public Task<(IncidentDto? Result, string? Error)> AddActionAsync(Guid incidentId, AddActionRequest request) =>
        PostAsync<AddActionRequest, IncidentDto>($"api/tablet/incidents/{incidentId}/actions", request);

    public Task<(IncidentDto? Result, string? Error)> AcknowledgeActionAsync(Guid incidentId, Guid actionId) =>
        PostNoBodyAsync<IncidentDto>($"api/tablet/incidents/{incidentId}/actions/{actionId}/acknowledge");

    public Task<(IncidentDto? Result, string? Error)> ResolveActionAsync(Guid incidentId, Guid actionId, string status) =>
        PostAsync<ResolveActionRequest, IncidentDto>($"api/tablet/incidents/{incidentId}/actions/{actionId}/resolve", new ResolveActionRequest(status));

    public Task<(IncidentDto? Result, string? Error)> AddObjectiveAsync(Guid incidentId, string text) =>
        PostAsync<AddObjectiveRequest, IncidentDto>($"api/tablet/incidents/{incidentId}/objectives", new AddObjectiveRequest(text));

    public Task<(IncidentDto? Result, string? Error)> AchieveObjectiveAsync(Guid incidentId, Guid objectiveId) =>
        PostNoBodyAsync<IncidentDto>($"api/tablet/incidents/{incidentId}/objectives/{objectiveId}/achieve");

    public Task<(IncidentDto? Result, string? Error)> ReopenObjectiveAsync(Guid incidentId, Guid objectiveId) =>
        PostNoBodyAsync<IncidentDto>($"api/tablet/incidents/{incidentId}/objectives/{objectiveId}/reopen");

    public Task<(IncidentDto? Result, string? Error)> AddRiskAsync(Guid incidentId, string description, string riskLevel, string? controlMeasure) =>
        PostAsync<AddRiskRequest, IncidentDto>($"api/tablet/incidents/{incidentId}/risks", new AddRiskRequest(description, riskLevel, controlMeasure));

    public Task<(IncidentDto? Result, string? Error)> ControlRiskAsync(Guid incidentId, Guid riskId) =>
        PostNoBodyAsync<IncidentDto>($"api/tablet/incidents/{incidentId}/risks/{riskId}/control");

    public Task<(IncidentDto? Result, string? Error)> ReopenRiskAsync(Guid incidentId, Guid riskId) =>
        PostNoBodyAsync<IncidentDto>($"api/tablet/incidents/{incidentId}/risks/{riskId}/reopen");

    public Task<(IncidentDto? Result, string? Error)> AddBaEntryControlPointAsync(Guid incidentId, string name, string stage) =>
        PostAsync<AddBaEntryControlPointRequest, IncidentDto>($"api/tablet/incidents/{incidentId}/ba-points",
            new AddBaEntryControlPointRequest(name, stage));

    public Task<(IncidentDto? Result, string? Error)> ClaimBaEntryControlPointAsync(Guid incidentId, Guid pointId) =>
        PostNoBodyAsync<IncidentDto>($"api/tablet/incidents/{incidentId}/ba-points/{pointId}/claim");

    public Task<(IncidentDto? Result, string? Error)> HandOverBaEntryControlPointAsync(Guid incidentId, Guid pointId) =>
        PostNoBodyAsync<IncidentDto>($"api/tablet/incidents/{incidentId}/ba-points/{pointId}/hand-over");

    public Task<(IncidentDto? Result, string? Error)> AddBaTeamAsync(
        Guid incidentId, Guid pointId, string name, string teamLeader, string? commsChannel, string? briefing, string? equipment) =>
        PostAsync<AddBaTeamRequest, IncidentDto>($"api/tablet/incidents/{incidentId}/ba-points/{pointId}/teams",
            new AddBaTeamRequest(name, teamLeader, commsChannel, briefing, equipment));

    public Task<(IncidentDto? Result, string? Error)> AddBaWearerAsync(
        Guid incidentId, Guid pointId, Guid teamId, string name, double cylinderPressureBar, int whistleMinutes) =>
        PostAsync<AddBaWearerRequest, IncidentDto>($"api/tablet/incidents/{incidentId}/ba-points/{pointId}/teams/{teamId}/wearers",
            new AddBaWearerRequest(name, cylinderPressureBar, whistleMinutes));

    public Task<(IncidentDto? Result, string? Error)> ExitBaWearerAsync(Guid incidentId, Guid pointId, Guid teamId, Guid wearerId) =>
        PostNoBodyAsync<IncidentDto>($"api/tablet/incidents/{incidentId}/ba-points/{pointId}/teams/{teamId}/wearers/{wearerId}/exit");

    public Task<(RouteResponseDto? Result, string? Error)> GetRouteAsync(Guid incidentId) =>
        GetAsync<RouteResponseDto>($"api/tablet/incidents/{incidentId}/route");

    public Task<string?> ReportLocationAsync(double latitude, double longitude) =>
        PostNoContentAsync("api/tablet/location", new UpdateDeviceLocationRequest(latitude, longitude));

    public Task<(IncidentDto? Result, string? Error)> StartNavigationAsync(Guid incidentId) =>
        PostNoBodyAsync<IncidentDto>($"api/tablet/incidents/{incidentId}/start-navigation");

    public Task<(OrganisationSettingsDto? Result, string? Error)> GetOrganisationSettingsAsync() =>
        GetAsync<OrganisationSettingsDto>("api/tablet/organisation-settings");

    public Task<(TabletDeviceDto? Result, string? Error)> GetDeviceAsync() =>
        GetAsync<TabletDeviceDto>("api/tablet/device");

    // Multipart, not JsonContent.Create -- the only endpoint whose body
    // isn't JSON, so this doesn't reuse PostAsync<TRequest, TResponse>.
    public async Task<(IncidentAttachmentDto? Result, string? Error)> UploadAttachmentAsync(Guid incidentId, Stream content, string fileName, string contentType)
    {
        try
        {
            using var request = await BuildRequestAsync(HttpMethod.Post, $"api/tablet/incidents/{incidentId}/attachments");
            if (request is null) return (default, RevokedError);

            using var form = new MultipartFormDataContent();
            using var fileContent = new StreamContent(content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(fileContent, "file", fileName);
            request.Content = form;

            using var response = await httpClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await tokenStore.ClearAsync();
                return (default, RevokedError);
            }
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync();
                return (default, string.IsNullOrWhiteSpace(detail) ? $"Upload failed ({(int)response.StatusCode})." : detail);
            }

            var result = await response.Content.ReadFromJsonAsync<IncidentAttachmentDto>(JsonOptions);
            return (result, null);
        }
        catch (Exception ex)
        {
            return (default, $"Could not reach Muster Hub Command ({ex.Message}).");
        }
    }

    // Buffers into a MemoryStream and disposes the response up front --
    // unlike the other endpoints here, the caller (a thumbnail's
    // ImageSource.FromStream factory) may re-read this stream more than
    // once or long after this method returns, so it can't stay tied to
    // the HttpResponseMessage's own lifetime.
    public async Task<(Stream? Result, string? Error)> DownloadAttachmentAsync(Guid attachmentId)
    {
        try
        {
            using var request = await BuildRequestAsync(HttpMethod.Get, $"api/tablet/incident-attachments/{attachmentId}");
            if (request is null) return (default, RevokedError);

            using var response = await httpClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await tokenStore.ClearAsync();
                return (default, RevokedError);
            }
            if (!response.IsSuccessStatusCode)
                return (default, $"Request failed ({(int)response.StatusCode}).");

            var buffer = new MemoryStream();
            await response.Content.CopyToAsync(buffer);
            buffer.Position = 0;
            return (buffer, null);
        }
        catch (Exception ex)
        {
            return (default, $"Could not reach Muster Hub Command ({ex.Message}).");
        }
    }

    private async Task<(T? Result, string? Error)> GetAsync<T>(string path)
    {
        try
        {
            using var request = await BuildRequestAsync(HttpMethod.Get, path);
            if (request is null) return (default, RevokedError);

            using var response = await httpClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await tokenStore.ClearAsync();
                return (default, RevokedError);
            }
            if (!response.IsSuccessStatusCode)
                return (default, $"Request to {path} failed ({(int)response.StatusCode}).");

            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
            return (result, null);
        }
        catch (Exception ex)
        {
            return (default, $"Could not reach Muster Hub Command ({ex.Message}).");
        }
    }

    private async Task<(TResponse? Result, string? Error)> PostAsync<TRequest, TResponse>(string path, TRequest body)
    {
        try
        {
            using var request = await BuildRequestAsync(HttpMethod.Post, path);
            if (request is null) return (default, RevokedError);
            request.Content = JsonContent.Create(body, options: JsonOptions);

            using var response = await httpClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await tokenStore.ClearAsync();
                return (default, RevokedError);
            }
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync();
                return (default, string.IsNullOrWhiteSpace(detail) ? $"Request to {path} failed ({(int)response.StatusCode})." : detail);
            }

            var result = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);
            return (result, null);
        }
        catch (Exception ex)
        {
            return (default, $"Could not reach Muster Hub Command ({ex.Message}).");
        }
    }

    // For a POST with no request body but a JSON response -- start-navigation
    // takes everything it needs from the device token itself.
    private async Task<(T? Result, string? Error)> PostNoBodyAsync<T>(string path)
    {
        try
        {
            using var request = await BuildRequestAsync(HttpMethod.Post, path);
            if (request is null) return (default, RevokedError);

            using var response = await httpClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await tokenStore.ClearAsync();
                return (default, RevokedError);
            }
            if (!response.IsSuccessStatusCode)
                return (default, $"Request to {path} failed ({(int)response.StatusCode}).");

            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
            return (result, null);
        }
        catch (Exception ex)
        {
            return (default, $"Could not reach Muster Hub Command ({ex.Message}).");
        }
    }

    // Separate from PostAsync<TRequest, TResponse> because
    // TabletLocationController returns 204 No Content -- ReadFromJsonAsync
    // throws on an empty body, so there's nothing to deserialize here.
    private async Task<string?> PostNoContentAsync<TRequest>(string path, TRequest body)
    {
        try
        {
            using var request = await BuildRequestAsync(HttpMethod.Post, path);
            if (request is null) return RevokedError;
            request.Content = JsonContent.Create(body, options: JsonOptions);

            using var response = await httpClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await tokenStore.ClearAsync();
                return RevokedError;
            }
            if (!response.IsSuccessStatusCode)
                return $"Request to {path} failed ({(int)response.StatusCode}).";

            return null;
        }
        catch (Exception ex)
        {
            return $"Could not reach Muster Hub Command ({ex.Message}).";
        }
    }

    private async Task<HttpRequestMessage?> BuildRequestAsync(HttpMethod method, string path)
    {
        var token = await tokenStore.GetAsync();
        if (token is null) return null;

        var request = new HttpRequestMessage(method, $"{AppConfig.ApiBaseUrl}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
