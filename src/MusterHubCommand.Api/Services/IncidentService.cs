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
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return incident;
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
