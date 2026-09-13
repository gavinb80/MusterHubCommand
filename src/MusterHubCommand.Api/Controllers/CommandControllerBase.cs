using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusterHubCommand.Api.Data.Entities;
using MusterHubCommand.Api.Services;

namespace MusterHubCommand.Api.Controllers;

// The control-room web console's base -- JWT-bearer only (see
// DeviceControllerBase for the tablet's parallel, device-token-only base;
// the two are never mixed on the same endpoint, since "who is this" means
// two completely different things for each).
[ApiController]
[Authorize(Policy = "RequireCommandEntitlement")]
public abstract class CommandControllerBase(
    ICurrentOrganisationAccessor organisationAccessor,
    ICurrentEmployeeAccessor currentEmployeeAccessor,
    OperatorPermissionChecker operatorChecker) : ControllerBase
{
    protected Guid OrganisationId => organisationAccessor.OrganisationId;

    // Same "helper returns a result or null" idiom as Rota's
    // RequirePermissionAsync -- returns the concrete ActionResult so a
    // caller in an ActionResult<T> action can return it directly.
    //
    // Bootstrapping is checked BEFORE resolving the caller's Employee.Id,
    // not after: the very first bootstrap action for a fresh org is running
    // the core-directory import that creates Employee rows in the first
    // place, so gating on "do you already have a linked Employee" would
    // make that action itself impossible to perform. Same ordering as
    // Rota's RotaControllerBase.RequirePermissionAsync.
    protected async Task<ActionResult?> RequireOperatorAsync()
    {
        if (await operatorChecker.IsBootstrappingAsync()) return null;

        var employeeId = await currentEmployeeAccessor.GetEmployeeIdAsync();
        if (employeeId is null) return Forbid();

        if (!await operatorChecker.IsOperatorAsync(employeeId.Value)) return Forbid();

        return null;
    }

    // Same idiom, for actions elevated above baseline operator access
    // (sector/hierarchy CRUD, granting operators) -- see CommandOperatorTier.
    protected async Task<ActionResult?> RequireIncidentCommanderAsync()
    {
        if (await operatorChecker.IsBootstrappingAsync()) return null;

        var employeeId = await currentEmployeeAccessor.GetEmployeeIdAsync();
        if (employeeId is null) return Forbid();

        if (!await operatorChecker.IsIncidentCommanderAsync(employeeId.Value)) return Forbid();

        return null;
    }

    // Cancel is the one carve-out that breaks the usual "Incident Commander
    // can do everything a baseline operator can, plus more" hierarchy: an
    // incident cancelled outright was raised in error, and that call
    // belongs to Control Room / Command Support -- the desk staff actually
    // triaging inbound calls, often before an Incident Commander is even
    // assigned -- not to the Commander. Deliberately excludes the
    // Commander tier rather than just not requiring it.
    protected async Task<ActionResult?> RequireCancelAccessAsync()
    {
        if (await operatorChecker.IsBootstrappingAsync()) return null;

        var employeeId = await currentEmployeeAccessor.GetEmployeeIdAsync();
        if (employeeId is null) return Forbid();

        var tier = await operatorChecker.GetTierAsync(employeeId.Value);
        if (tier is null || tier == CommandOperatorTier.IncidentCommander) return Forbid();

        return null;
    }

    protected async Task<Guid?> CurrentEmployeeIdAsync() => await currentEmployeeAccessor.GetEmployeeIdAsync();
}
