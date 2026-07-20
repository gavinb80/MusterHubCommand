namespace MusterHubCommand.Api.Services;

public interface ICurrentOrganisationAccessor
{
    // Empty when there's no authenticated request in scope (e.g. outside an
    // HTTP request, such as a background job -- those must resolve the
    // organisation explicitly from their own work item, not from this).
    Guid OrganisationId { get; }
}
