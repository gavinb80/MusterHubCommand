using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusterHubCommand.Api.Configuration;
using MusterHubCommand.Api.Data;

namespace MusterHubCommand.Api.Services;

// The one V1 push case named in the plan: "new incident at your station."
// Same relay pattern as Rota's own CoreNotificationService -- Command knows
// WHO should hear (core user ids ride on Employee.PersonId, resolved via
// EmployeeStationAssignment), core knows HOW to reach them (device tokens
// never leave core). Deliberately best-effort: an incident push must never
// fail or slow down incident creation because a push provider or the relay
// hiccuped. Plain alert only, no deep link -- there's no phone-app screen
// for a control-room incident to land on, unlike Rota/Skills' own pushes.
public class CoreNotificationService(
    ApplicationDbContext db,
    HttpClient httpClient,
    IOptions<CoreAuthOptions> coreOptions,
    ILogger<CoreNotificationService> logger)
{
    private record RelayRequest(Guid UserId, string Title, string Body);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(coreOptions.Value.NotificationsUri)
        && !string.IsNullOrWhiteSpace(coreOptions.Value.NotificationApiKey);

    // Everyone with an active station assignment at this unit -- the same
    // audience Setup's device pairing scopes a tablet to, so "who gets
    // paged" and "which tablet sees it" always agree. Takes organisationId
    // explicitly and queries with IgnoreQueryFilters(), same reasoning as
    // IncidentService throughout: the caller is usually
    // IntegrationIncidentsController, which is [AllowAnonymous] -- the
    // ambient tenant filter resolves to Guid.Empty there and would
    // silently return zero employees for every anonymous-key push. Found
    // via live verification: the relay's LastUsedAtUtc never moved despite
    // IsConfigured being true and the query returning no rows.
    public async Task NotifyStationEmployeesAsync(Guid organisationId, Guid orgUnitId, string title, string body)
    {
        if (!IsConfigured) return;

        var personIds = await db.EmployeeStationAssignments.IgnoreQueryFilters()
            .Where(a => a.OrganisationId == organisationId && a.OrgUnitId == orgUnitId && a.Employee!.PersonId != null)
            .Select(a => a.Employee!.PersonId!.Value)
            .Distinct()
            .ToListAsync();

        foreach (var personId in personIds) await SendAsync(personId, title, body);
    }

    private async Task SendAsync(Guid personId, string title, string body)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                coreOptions.Value.NotificationsUri, new RelayRequest(personId, title, body));
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Core notification relay returned {Status} for person {PersonId}", response.StatusCode, personId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Core notification relay call failed for person {PersonId}", personId);
        }
    }
}
