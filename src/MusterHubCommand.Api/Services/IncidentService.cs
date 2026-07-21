using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Services;

// "No station found with code '…'" -- a validation failure, not a server
// error, whichever caller triggers it (Vision's own push, or an operator's
// manual form).
public class IncidentValidationException(string message) : Exception(message);

// One code path for both callers named in the plan: Vision's push/update/
// delete API and the control-room web console's own manual forms. Takes
// OrganisationId as an explicit parameter throughout rather than reading it
// off ICurrentOrganisationAccessor, since IntegrationIncidentsController is
// [AllowAnonymous] -- there is no ambient organisation until the caller's
// API key has already resolved one.
public class IncidentService(ApplicationDbContext db, CoreNotificationService notificationService)
{
    public Task<Incident?> FindByExternalReferenceAsync(Guid organisationId, string externalReference, CancellationToken ct = default) =>
        Query(organisationId).FirstOrDefaultAsync(i => i.ExternalReference == externalReference, ct);

    public Task<Incident?> FindByIdAsync(Guid organisationId, Guid id, CancellationToken ct = default) =>
        Query(organisationId).FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<List<Incident>> ListAsync(Guid organisationId, Guid? orgUnitId = null, bool activeOnly = false, CancellationToken ct = default) =>
        Query(organisationId)
            .Where(i => !activeOnly || i.Status == IncidentStatus.Open)
            .Where(i => orgUnitId == null || i.OrgUnitId == orgUnitId)
            .OrderByDescending(i => i.StartedAtUtc)
            .ToListAsync(ct);

    private IQueryable<Incident> Query(Guid organisationId) =>
        db.Incidents.IgnoreQueryFilters()
            .Include(i => i.Appliances)
            .Include(i => i.Updates)
            .Include(i => i.OrgUnit)
            .Where(i => i.OrganisationId == organisationId);

    public async Task<Incident> CreateOrUpsertAsync(Guid organisationId, CreateIncidentRequest request, CancellationToken ct = default)
    {
        var stationId = await ResolveStationIdAsync(organisationId, request.StationCode, ct)
            ?? throw new IncidentValidationException($"No station found with code '{request.StationCode}'.");

        var existing = await Query(organisationId).FirstOrDefaultAsync(i => i.ExternalReference == request.ExternalReference, ct);
        if (existing is not null)
        {
            existing.IncidentType = request.IncidentType;
            existing.Description = request.Description;
            existing.Address = request.Address;
            existing.Latitude = request.Latitude;
            existing.Longitude = request.Longitude;
            existing.OrgUnitId = stationId;
            if (request.StartedAtUtc is { } started) existing.StartedAtUtc = started;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            existing.OrgUnit = await LoadOrgUnitAsync(stationId, ct);
            return existing;
        }

        var incident = new Incident
        {
            OrganisationId = organisationId,
            ExternalReference = request.ExternalReference,
            IncidentType = request.IncidentType,
            Description = request.Description,
            Address = request.Address,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            OrgUnitId = stationId,
            StartedAtUtc = request.StartedAtUtc ?? DateTimeOffset.UtcNow,
        };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync(ct);
        incident.OrgUnit = await LoadOrgUnitAsync(stationId, ct);

        // Only ever fires for a genuinely new incident, never the upsert-
        // existing branch above -- re-pushing an update from Vision (a
        // status change, an amended address) must not re-page the whole
        // crew's phones every time.
        await notificationService.NotifyStationEmployeesAsync(
            organisationId, stationId, $"New incident: {incident.IncidentType}", incident.Address ?? incident.OrgUnit?.Name ?? "");

        return incident;
    }

    public async Task<Incident?> PatchAsync(Guid organisationId, Guid incidentId, UpdateIncidentRequest request, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        if (request.IncidentType is not null) incident.IncidentType = request.IncidentType;
        if (request.Description is not null) incident.Description = request.Description;
        if (request.Address is not null) incident.Address = request.Address;
        if (request.Latitude is not null) incident.Latitude = request.Latitude;
        if (request.Longitude is not null) incident.Longitude = request.Longitude;
        if (request.StationCode is not null)
        {
            incident.OrgUnitId = await ResolveStationIdAsync(organisationId, request.StationCode, ct)
                ?? throw new IncidentValidationException($"No station found with code '{request.StationCode}'.");
        }
        if (request.Status is { } status)
        {
            incident.Status = status;
            incident.ClosedAtUtc = status == IncidentStatus.Open ? null : incident.ClosedAtUtc ?? DateTimeOffset.UtcNow;
        }
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        if (request.StationCode is not null) incident.OrgUnit = await LoadOrgUnitAsync(incident.OrgUnitId, ct);
        return incident;
    }

    // Soft: marks Cancelled rather than removing the row, so a tablet with
    // the incident open shows "cancelled" instead of it vanishing.
    public async Task<Incident?> CancelAsync(Guid organisationId, Guid incidentId, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        incident.Status = IncidentStatus.Cancelled;
        incident.ClosedAtUtc ??= DateTimeOffset.UtcNow;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return incident;
    }

    // Replace-whole, not incremental -- same pattern as Rota's
    // SetAvailabilityContract -- so there's never drift between what Vision
    // thinks is attending and what the tablet shows after a make-up or
    // stand-down.
    public async Task<Incident?> SetAppliancesAsync(Guid organisationId, Guid incidentId, SetAppliancesRequest request, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        // Snapshotted before RemoveRange below so the timeline only gets an
        // entry for what actually changed -- Vision resyncing the same
        // attendance it already pushed must not spam one update per
        // appliance every time.
        var previousStatus = incident.Appliances.ToDictionary(a => a.Callsign, a => a.Status, StringComparer.OrdinalIgnoreCase);

        db.IncidentAppliances.RemoveRange(incident.Appliances);
        // AddRange explicitly, not just an assignment to the navigation --
        // IncidentAppliance.Id is client-generated and already non-default
        // by the time SaveChanges' graph-fixup would see it, so implicit
        // tracking reads it as Modified rather than Added and emits an
        // UPDATE against a row that doesn't exist yet, throwing a
        // DbUpdateConcurrencyException. Found via live verification.
        var newAppliances = request.Appliances
            .Select(a => new IncidentAppliance { IncidentId = incident.Id, Callsign = a.Callsign, Status = a.Status })
            .ToList();
        db.IncidentAppliances.AddRange(newAppliances);
        incident.Appliances = newAppliances;

        foreach (var appliance in newAppliances)
        {
            if (!previousStatus.TryGetValue(appliance.Callsign, out var priorStatus) || priorStatus != appliance.Status)
                QueueResourceChangeUpdate(incident, IncidentUpdateSource.ControlRoom, $"{appliance.Callsign} {appliance.Status}");
        }
        var newCallsigns = newAppliances.Select(a => a.Callsign).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var callsign in previousStatus.Keys.Where(c => !newCallsigns.Contains(c)))
            QueueResourceChangeUpdate(incident, IncidentUpdateSource.ControlRoom, $"{callsign} removed from attendance");

        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return incident;
    }

    // Upsert-one, deliberately not SetAppliancesAsync's replace-whole
    // semantics -- that's for Control/Vision's full push; a single tablet
    // reporting its own status (e.g. "Start navigation" -> EnRoute) must
    // never clobber every other appliance's attendance row. Case-
    // insensitive match on Callsign since it's operator-typed in two
    // different places (Setup's Device.Callsign, and whatever Vision/
    // Control originally pushed) and casing drift shouldn't fork a device
    // into a second, orphaned attendance row.
    public async Task<Incident?> SetSingleApplianceStatusAsync(
        Guid organisationId, Guid incidentId, string callsign, ApplianceStatus status, CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        var existing = incident.Appliances.FirstOrDefault(a => string.Equals(a.Callsign, callsign, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            // Only a genuine transition earns a timeline entry -- the
            // geofence check re-runs on every location report and would
            // otherwise re-log the same already-current status repeatedly.
            if (existing.Status != status)
            {
                existing.Status = status;
                existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
                QueueResourceChangeUpdate(incident, IncidentUpdateSource.Crew, $"{callsign} {status}");
            }
        }
        else
        {
            // Added explicitly via AddRange rather than relying on the
            // Appliances navigation's own change tracking -- see
            // SetAppliancesAsync's own comment on this same gotcha
            // (client-generated Id reads as Modified, not Added).
            var appliance = new IncidentAppliance { IncidentId = incident.Id, Callsign = callsign, Status = status };
            db.IncidentAppliances.Add(appliance);
            incident.Appliances.Add(appliance);
            QueueResourceChangeUpdate(incident, IncidentUpdateSource.Crew, $"{callsign} {status}");
        }
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return incident;
    }

    // Writes into the previously-reserved-but-unused ResourceChange slot
    // (IncidentUpdateType.ResourceChange) so a status change becomes a
    // real, permanent timeline entry -- IncidentAppliance itself only ever
    // stores CURRENT status, so this update row is the only place a
    // Mobilised -> EnRoute -> OnScene history survives. Text matches the
    // same bare enum string the status pills already render elsewhere
    // (e.g. "KV57P1 EnRoute"), not a separately-invented phrasing. Only
    // adds to db.IncidentUpdates, not also to incident.Updates -- EF's own
    // change-tracker fixup already links a newly-Added entity into an
    // already-tracked principal's navigation collection once its FK is
    // set, so adding it a second time here double-counted every entry in
    // the Incident this method returns. Found via live test failure: two
    // appliances dispatched together came back as four ResourceChange
    // updates, not two.
    private void QueueResourceChangeUpdate(Incident incident, IncidentUpdateSource source, string text)
    {
        db.IncidentUpdates.Add(new IncidentUpdate
        {
            IncidentId = incident.Id,
            Source = source,
            Text = text,
            UpdateType = IncidentUpdateType.ResourceChange,
        });
    }

    // Append-only -- nothing here ever edits or removes a prior update.
    public async Task<Incident?> AddUpdateAsync(
        Guid organisationId, Guid incidentId, IncidentUpdateSource source,
        string? authorName, Guid? authorEmployeeId, string text, IncidentUpdateType updateType,
        CancellationToken ct = default)
    {
        var incident = await Query(organisationId).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return null;

        db.IncidentUpdates.Add(new IncidentUpdate
        {
            IncidentId = incident.Id,
            Source = source,
            AuthorName = authorName,
            AuthorEmployeeId = authorEmployeeId,
            Text = text,
            UpdateType = updateType,
        });
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await db.Entry(incident).Collection(i => i.Updates).LoadAsync(ct);
        return incident;
    }

    // db.Entry(...).Reference(...).LoadAsync() applies the ambient tenant
    // filter, which resolves to Guid.Empty for IntegrationIncidentsController's
    // anonymous callers -- silently loading nothing rather than throwing, so
    // the bug shows up as a blank OrgUnitName, not an error. IgnoreQueryFilters
    // sidesteps it, matching the explicit organisationId this whole service
    // already threads through instead of relying on ambient state.
    private Task<OrgUnit?> LoadOrgUnitAsync(Guid orgUnitId, CancellationToken ct) =>
        db.OrgUnits.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == orgUnitId, ct);

    private Task<Guid?> ResolveStationIdAsync(Guid organisationId, string stationCode, CancellationToken ct) =>
        db.OrgUnits.IgnoreQueryFilters()
            .Where(u => u.OrganisationId == organisationId && u.Code == stationCode)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
}
