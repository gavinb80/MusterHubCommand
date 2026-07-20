namespace MusterHubCommand.Api.Data;

// Every domain table implements this. OrganisationId is an opaque reference
// to MusterHub core's Service.Id -- there is no foreign key across the two
// databases, just a shared identifier carried in the validated JWT's org_id
// claim (web console) or stamped onto the Device at pairing time (tablet).
// See ApplicationDbContext for how this is enforced as a global,
// default-deny query filter rather than a per-query convention.
public interface ITenantScoped
{
    Guid OrganisationId { get; set; }
}
