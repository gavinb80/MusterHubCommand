using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MusterHubCommand.Api.Data;

namespace MusterHubCommand.Api.Services;

public class HttpContextCurrentEmployeeAccessor(IHttpContextAccessor httpContextAccessor, ApplicationDbContext db)
    : ICurrentEmployeeAccessor
{
    public async Task<Guid?> GetEmployeeIdAsync(CancellationToken cancellationToken = default)
    {
        var claim = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (claim is null || !Guid.TryParse(claim, out var personId)) return null;

        return await db.Employees
            .Where(e => e.PersonId == personId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
