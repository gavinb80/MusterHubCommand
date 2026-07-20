using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Data;

// Computes Path for newly-inserted OrgUnits (including a whole subtree
// inserted in one SaveChanges call, parent-first) -- same interceptor as
// Rota/Skills' own OrgUnitPathInterceptor. Directory sync always saves
// groups before stations that reference them, so this only ever needs to
// resolve forward, never re-parent an existing unit.
public class OrgUnitPathInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ComputePathsForNewUnits(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ComputePathsForNewUnits(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void ComputePathsForNewUnits(DbContext? context)
    {
        if (context is null) return;

        var added = context.ChangeTracker.Entries<OrgUnit>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        if (added.Count == 0) return;

        var resolved = new Dictionary<Guid, string>();
        var remaining = new List<OrgUnit>(added);
        bool progressed = true;

        while (remaining.Count > 0 && progressed)
        {
            progressed = false;

            foreach (var unit in remaining.ToList())
            {
                string? parentPath;

                if (unit.ParentId is null)
                {
                    parentPath = "";
                }
                else if (resolved.TryGetValue(unit.ParentId.Value, out var alreadyResolved))
                {
                    parentPath = alreadyResolved;
                }
                else
                {
                    var parentEntry = context.ChangeTracker.Entries<OrgUnit>()
                        .FirstOrDefault(e => e.Entity.Id == unit.ParentId.Value && e.State == EntityState.Unchanged);
                    parentPath = parentEntry?.Entity.Path;
                }

                if (parentPath is null) continue; // parent not resolvable yet -- try again next pass

                var label = unit.Id.ToString("N"); // ltree labels can't contain hyphens
                unit.Path = parentPath.Length == 0 ? label : $"{parentPath}.{label}";
                resolved[unit.Id] = unit.Path;
                remaining.Remove(unit);
                progressed = true;
            }
        }

        if (remaining.Count > 0)
        {
            throw new InvalidOperationException(
                "Could not resolve OrgUnit path: a ParentId doesn't reference a unit already saved to the " +
                "database or included in this same SaveChanges call.");
        }
    }
}
