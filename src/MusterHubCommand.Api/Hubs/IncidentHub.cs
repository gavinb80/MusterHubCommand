using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MusterHubCommand.Api.Hubs;

// A cache-invalidation nudge, not a data pipe -- server->client messages
// carry only an id, never a duplicated DTO payload. The client's existing
// apiFetch + react-query invalidateQueries machinery does the actual fetch,
// so there's no second data shape to keep in sync with the REST DTOs.
// Web console only -- the tablet's own ~15s poll is a separate, already-
// tolerable cadence for a device meant to be glanced at fireground-side.
//
// Same policy every CommandControllerBase-derived controller already
// requires -- a valid MusterHub identity without the Command entitlement
// shouldn't be able to open a connection here either. Default scheme (JWT
// bearer, see Program.cs) -- the Device scheme is never wired to this hub.
[Authorize(Policy = "RequireCommandEntitlement")]
public class IncidentHub : Hub
{
    public static string OrgGroup(Guid organisationId) => $"org-{organisationId}";
    public static string IncidentGroup(Guid incidentId) => $"incident-{incidentId}";

    // Every connected operator joins their own org's group for as long as
    // the connection lives -- the incidents list wants "anything changed
    // for my org", not a per-incident subscription.
    public override async Task OnConnectedAsync()
    {
        if (OrganisationId is { } organisationId)
            await Groups.AddToGroupAsync(Context.ConnectionId, OrgGroup(organisationId));
        await base.OnConnectedAsync();
    }

    // Explicit join/leave, called as an operator opens/closes a specific
    // incident's detail page -- narrower than the org-wide group, so a
    // busy shift with a dozen open incidents doesn't push every operator
    // updates for incidents they aren't even looking at.
    public Task JoinIncident(Guid incidentId) => Groups.AddToGroupAsync(Context.ConnectionId, IncidentGroup(incidentId));

    public Task LeaveIncident(Guid incidentId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, IncidentGroup(incidentId));

    // Same "org_id" claim HttpContextCurrentOrganisationAccessor reads.
    // Null (unparseable) rather than throwing: OnConnectedAsync silently
    // skips joining any group in that case rather than failing the whole
    // connection -- shouldn't actually happen given [Authorize] above, but
    // isn't worth taking the connection down over if it somehow does.
    private Guid? OrganisationId =>
        Guid.TryParse(Context.User?.FindFirstValue("org_id"), out var id) ? id : null;
}
