using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Data;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Services;

// The permission checks the web console needs: is this employee an
// operator at all, and if so, at which tier. Same bootstrapping escape
// hatch as Rota's EmployeePermissionChecker -- an organisation with zero
// CommandOperator grants treats every signed-in employee as an operator
// (and, for tier purposes, as an Incident Commander -- the highest tier),
// so the very first person to open Command can do everything, including
// granting the role to a colleague, without needing an existing operator
// to have granted it to them first.
public class OperatorPermissionChecker(ApplicationDbContext db)
{
    public async Task<bool> IsBootstrappingAsync(CancellationToken cancellationToken = default) =>
        !await db.CommandOperators.AnyAsync(cancellationToken);

    public async Task<bool> IsOperatorAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (await IsBootstrappingAsync(cancellationToken)) return true;
        return await db.CommandOperators.AnyAsync(o => o.EmployeeId == employeeId, cancellationToken);
    }

    public async Task<CommandOperatorTier?> GetTierAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (await IsBootstrappingAsync(cancellationToken)) return CommandOperatorTier.IncidentCommander;
        var op = await db.CommandOperators.FirstOrDefaultAsync(o => o.EmployeeId == employeeId, cancellationToken);
        return op?.Tier;
    }

    public async Task<bool> IsIncidentCommanderAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await GetTierAsync(employeeId, cancellationToken) == CommandOperatorTier.IncidentCommander;

    // Closes the bootstrap window deliberately rather than leaving it open
    // until whoever happens to visit Setup first claims it. Mirrors Rota
    // and Skills' own EnsureModuleAdminGrantedAsync: core's designated
    // module admin (the "Command" tickbox on the user's own admin page,
    // arriving here as the JWT's multi-valued module_admin claim) gets
    // Incident Commander -- Command's top tier -- the moment they first
    // sign in, whether or not the org has already bootstrapped, as long as
    // they don't already hold some operator grant of their own.
    public async Task EnsureModuleAdminGrantedAsync(
        Guid organisationId, Guid employeeId, bool isDesignatedModuleAdmin, CancellationToken cancellationToken = default)
    {
        if (!isDesignatedModuleAdmin) return;

        var isBootstrapping = await IsBootstrappingAsync(cancellationToken);
        if (!isBootstrapping && await IsOperatorAsync(employeeId, cancellationToken)) return;

        var existing = await db.CommandOperators.FirstOrDefaultAsync(o => o.EmployeeId == employeeId, cancellationToken);
        if (existing is null)
        {
            db.CommandOperators.Add(new CommandOperator
            {
                OrganisationId = organisationId,
                EmployeeId = employeeId,
                Tier = CommandOperatorTier.IncidentCommander,
            });
        }
        else
        {
            existing.Tier = CommandOperatorTier.IncidentCommander;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
