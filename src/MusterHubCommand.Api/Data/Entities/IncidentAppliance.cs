namespace MusterHubCommand.Api.Data.Entities;

public enum ApplianceStatus
{
    Mobilised,
    EnRoute,
    OnScene,
    StoodDown,
}

// Attendance for one incident -- fed by control, replaced whole on every
// push (see IntegrationIncidentsController.SetAppliances) rather than
// patched incrementally, so there's never drift between what Vision thinks
// is attending and what the tablet shows after a make-up or stand-down.
public class IncidentAppliance
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public required string Callsign { get; set; }
    public ApplianceStatus Status { get; set; } = ApplianceStatus.Mobilised;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
