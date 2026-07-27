namespace MusterHubCommand.Api.Data.Entities;

public enum ApplianceStatus
{
    Mobilised,
    EnRoute,
    OnScene,
    StoodDown,
}

// Appliance vs. a resource that isn't itself a fire appliance -- an
// officer's car or a specialist unit (USAR, HazMat...) attending the same
// incident. Purely a display/grouping tag Control sets by hand; Vision
// never sends it (SetAppliancesRequest has no such field), so it isn't
// something that can drift out of sync with Vision's own view the way
// Status could.
public enum ResourceKind
{
    Appliance,
    OfficerVehicle,
    Specialist,
}

// Attendance for one incident -- fed by control, replaced whole on every
// push (see IntegrationIncidentsController.SetAppliances) rather than
// patched incrementally, so there's never drift between what Vision thinks
// is attending and what the tablet shows after a make-up or stand-down.
// SectorId and ResourceKind are hand-set by Control and NOT part of that
// Vision push -- IncidentService.SetAppliancesAsync carries both forward
// by callsign across the replace so a routine resync doesn't silently wipe
// them (see that method's own comment).
public class IncidentAppliance
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public required string Callsign { get; set; }
    public ApplianceStatus Status { get; set; } = ApplianceStatus.Mobilised;
    public ResourceKind ResourceKind { get; set; } = ResourceKind.Appliance;

    // Null = "Unassigned" -- not every appliance needs a sector, especially
    // early in an incident before Control has organised one.
    public Guid? SectorId { get; set; }
    public IncidentSector? Sector { get; set; }

    // Same dual-mode pattern as IncidentSector.PersonInCharge* -- an
    // officer in charge of an appliance is a person attribute independent
    // of who's in charge of a sector or the whole incident, so it lives
    // here rather than only being derivable from a sector assignment.
    public Guid? OfficerInChargeEmployeeId { get; set; }
    public Employee? OfficerInChargeEmployee { get; set; }
    public string? OfficerInChargeName { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
