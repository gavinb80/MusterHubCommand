namespace MusterHubCommand.Api.Data.Entities;

// Metadata/authorisation anchor for a photo or document attached to an
// incident -- the bytes themselves live in IFileStorage (see
// Services/FileStorage.cs), StoragePath is server-generated
// ({OrgId}/{IncidentId}/{AttachmentId}{ext}), never user input.
//
// Uploads come from two different auth models: a web operator (JWT,
// carries an employee identity) or a tablet (device token, no employee
// identity). Two plain nullable FKs -- never both set -- rather than a
// polymorphic "uploaded by" abstraction; there's no third source today.
public class IncidentAttachment : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }

    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string StoragePath { get; set; }
    public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Guid? UploadedByEmployeeId { get; set; }
    public Employee? UploadedByEmployee { get; set; }
    public Guid? UploadedByDeviceId { get; set; }
    public Device? UploadedByDevice { get; set; }
}
