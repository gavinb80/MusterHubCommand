using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Contracts;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Routing;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// The control-room web console's own authoring surface -- same
// IncidentService underneath IntegrationIncidentsController, so a manually
// keyed incident and a Vision-pushed one behave identically. Operator-gated
// throughout: creating/editing incidents and pushing updates is exactly as
// sensitive as provisioning a device, see DevicesController.
[Route("api/incidents")]
public class IncidentsController(
    IncidentService incidentService,
    ApplicationDbContext db,
    RoutingService routingService,
    ICurrentOrganisationAccessor organisationAccessor,
    ICurrentEmployeeAccessor currentEmployeeAccessor,
    OperatorPermissionChecker operatorChecker)
    : CommandControllerBase(organisationAccessor, currentEmployeeAccessor, operatorChecker)
{
    [HttpGet]
    public async Task<ActionResult<List<IncidentSummaryDto>>> List([FromQuery] Guid? orgUnitId, [FromQuery] bool activeOnly = true)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var incidents = await incidentService.ListAsync(OrganisationId, orgUnitId, activeOnly);
        return Ok(incidents.Select(i => i.ToSummaryDto()));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<IncidentDto>> Get(Guid id)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        return incident is null ? NotFound() : Ok(await incident.ToDtoWithLocationsAsync(db));
    }

    [HttpPost]
    public async Task<ActionResult<IncidentDto>> Create(CreateIncidentRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        try
        {
            var incident = await incidentService.CreateOrUpsertAsync(OrganisationId, request);
            return Ok(incident.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<IncidentDto>> Update(Guid id, UpdateIncidentRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        try
        {
            var updated = await incidentService.PatchAsync(OrganisationId, id, request);
            return updated is null ? NotFound() : Ok(updated.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.CancelAsync(OrganisationId, id);
        return updated is null ? NotFound() : NoContent();
    }

    [HttpPut("{id}/appliances")]
    public async Task<ActionResult<IncidentDto>> SetAppliances(Guid id, SetAppliancesRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.SetAppliancesAsync(OrganisationId, id, request);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPost("{id}/sectors")]
    public async Task<ActionResult<IncidentDto>> AddSector(Guid id, AddSectorRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Sector name is required.");

        try
        {
            var updated = await incidentService.AddSectorAsync(
                OrganisationId, id, request.Name.Trim(),
                request.ParentId, request.PersonInChargeEmployeeId, request.PersonInChargeName);
            return updated is null ? NotFound() : Ok(updated.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPatch("{id}/sectors/{sectorId}")]
    public async Task<ActionResult<IncidentDto>> UpdateSector(Guid id, Guid sectorId, UpdateSectorRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Sector name is required.");

        try
        {
            var updated = await incidentService.UpdateSectorAsync(
                OrganisationId, id, sectorId, request.Name.Trim(),
                request.ParentId, request.PersonInChargeEmployeeId, request.PersonInChargeName);
            return updated is null ? NotFound() : Ok(updated.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("{id}/sectors/{sectorId}")]
    public async Task<ActionResult<IncidentDto>> DeleteSector(Guid id, Guid sectorId)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.DeleteSectorAsync(OrganisationId, id, sectorId);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPatch("{id}/appliances/{applianceId}/sector")]
    public async Task<ActionResult<IncidentDto>> AssignApplianceSector(Guid id, Guid applianceId, AssignSectorRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        try
        {
            var updated = await incidentService.AssignApplianceSectorAsync(OrganisationId, id, applianceId, request.SectorId);
            return updated is null ? NotFound() : Ok(updated.ToDto());
        }
        catch (IncidentValidationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPatch("{id}/appliances/{applianceId}/resource-kind")]
    public async Task<ActionResult<IncidentDto>> SetApplianceResourceKind(Guid id, Guid applianceId, SetResourceKindRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.SetApplianceResourceKindAsync(OrganisationId, id, applianceId, request.ResourceKind);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPatch("{id}/appliances/{applianceId}/officer")]
    public async Task<ActionResult<IncidentDto>> SetApplianceOfficer(Guid id, Guid applianceId, SetApplianceOfficerRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.SetApplianceOfficerAsync(
            OrganisationId, id, applianceId, request.OfficerInChargeEmployeeId, request.OfficerInChargeName);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPost("{id}/updates")]
    public async Task<ActionResult<IncidentDto>> AddUpdate(Guid id, AddIncidentUpdateRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.AddUpdateAsync(
            OrganisationId, id, IncidentUpdateSource.ControlRoom,
            request.AuthorName, null, request.Text, request.UpdateType);
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    [HttpPost("{id}/updates/{updateId}/acknowledge")]
    public async Task<ActionResult<IncidentDto>> AcknowledgeUpdate(Guid id, Guid updateId, AcknowledgeUpdateRequest request)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var updated = await incidentService.AcknowledgeUpdateAsync(OrganisationId, id, updateId, request.AcknowledgedByName ?? "Control Room");
        return updated is null ? NotFound() : Ok(updated.ToDto());
    }

    // Same route computation as the tablet's own GET .../route, but for a
    // control-room operator picking any device (not just "this tablet's
    // own") to check against -- e.g. comparing which of two attending
    // appliances is genuinely closer once road access is accounted for.
    [HttpGet("{id}/route")]
    public async Task<ActionResult<RouteResponseDto>> GetRoute(Guid id, [FromQuery] Guid deviceId)
    {
        if (await RequireOperatorAsync() is ActionResult denied) return denied;

        var incident = await incidentService.FindByIdAsync(OrganisationId, id);
        if (incident is null) return NotFound();
        if (incident.Latitude is null || incident.Longitude is null)
            return Ok(new RouteResponseDto(false, "This incident has no location to route to.", null, null, null, null));

        var device = await db.Devices.Include(d => d.VehicleProfile).FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device is null) return NotFound();
        if (device.CurrentLatitude is null || device.CurrentLongitude is null)
            return Ok(new RouteResponseDto(false, "That device hasn't reported a location yet.", null, null, null, null));

        var (result, failure) = await routingService.ComputeRouteAsync(
            device.CurrentLatitude.Value, device.CurrentLongitude.Value,
            incident.Latitude.Value, incident.Longitude.Value, device.VehicleProfile);

        return Ok(result is not null ? result.ToDto() : failure!.Value.ToUnavailableDto());
    }
}
