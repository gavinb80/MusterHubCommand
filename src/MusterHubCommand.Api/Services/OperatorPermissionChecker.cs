using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Data;

namespace MusterHubCommand.Api.Services;

// The one permission check the web console needs: is this employee a
// Control Room Operator. Same bootstrapping escape hatch as Rota's
// EmployeePermissionChecker -- an organisation with zero CommandOperator
// grants treats every signed-in employee as one, so the very first person
// to open Command can grant the role (to themselves or a colleague) without
// needing an existing operator to have granted it to them first.
public class OperatorPermissionChecker(ApplicationDbContext db)
{
    public async Task<bool> IsBootstrappingAsync(CancellationToken cancellationToken = default) =>
        !await db.CommandOperators.AnyAsync(cancellationToken);

    public async Task<bool> IsOperatorAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (await IsBootstrappingAsync(cancellationToken)) return true;
        return await db.CommandOperators.AnyAsync(o => o.EmployeeId == employeeId, cancellationToken);
    }
}
