namespace MusterHubCommand.Api.Data.Entities;

// PersonId is an opaque reference to core's User.Id -- Command holds no user
// record of its own. Nullable deliberately: an import can create an employee
// record ahead of anyone actually logging into Command's web console, and
// link PersonId later once each person first authenticates there.
public class Employee : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public Guid? PersonId { get; set; }

    public required string DisplayName { get; set; }
    public string? EmployeeNumber { get; set; }
}
