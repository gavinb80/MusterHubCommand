namespace MusterHubCommand.Api.Data.Entities;

// Which station(s) an employee belongs to -- exists purely so the
// new-incident push (NotificationService.NotifyStationEmployeesAsync)
// knows whose phones to page. Unlike Rota/Skills' own
// EmployeeStationAssignment, deliberately no ValidFrom/ValidTo: Command
// does no rostering or historical reporting against this, it only ever
// needs "who is at this station right now," so each directory sync
// replaces an employee's whole assignment set rather than date-ranging it.
public class EmployeeStationAssignment : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public Guid OrgUnitId { get; set; }
    public OrgUnit? OrgUnit { get; set; }

    public bool IsHome { get; set; }
}
