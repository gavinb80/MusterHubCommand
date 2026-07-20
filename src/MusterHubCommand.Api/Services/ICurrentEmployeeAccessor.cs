namespace MusterHubCommand.Api.Services;

// Resolves the caller's own Command Employee.Id from their JWT (via
// Employee.PersonId matching core's User.Id in the token's NameIdentifier
// claim) -- the web console only; a device-authenticated tablet request has
// no person behind it, GetEmployeeIdAsync returns null for those.
public interface ICurrentEmployeeAccessor
{
    Task<Guid?> GetEmployeeIdAsync(CancellationToken cancellationToken = default);
}
