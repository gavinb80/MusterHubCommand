namespace MusterHubCommand.Api.Data.Entities;

// The three BA Operating Procedures stages, per National Operational
// Guidance -- I: entry control on the appliance/bridgehead with no
// guideline; II: a guideline used; III: main and emergency lines. Named
// after the real terminology (Stage I/II/III), not an arbitrary
// Low/Medium/High-style scale.
public enum BaStage
{
    I,
    II,
    III,
}

// The board itself. An incident can run more than one at once (e.g.
// "Alpha" and "Bravo" at a large multi-appliance incident), each tracked
// independently -- not a single board per incident.
public class BaEntryControlPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    // A location or a callsign like "Alpha" -- whichever the ECO actually
    // uses to identify their own point, not a fixed vocabulary.
    public required string Name { get; set; }
    public BaStage Stage { get; set; }

    // Null means unclaimed. The device that creates a point owns it and,
    // while it does, is the only device that can see or write to it (and
    // can't create a second one) -- a tablet is physically at one entry
    // control point, not several at once. Hand-over clears this back to
    // null so another device can claim it; see IncidentService's
    // Add/Claim/HandOverBaEntryControlPointAsync.
    public Guid? OwningDeviceId { get; set; }
    public Device? OwningDevice { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<BaTeam> Teams { get; set; } = [];
}

// A crew committed together under this point -- "Alpha 1", "Alpha 2" by
// convention (the point's own name plus a number), but Name stays free
// text rather than auto-numbered: an ECO's own board is the source of
// truth, not a naming scheme this app invents.
public class BaTeam
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EntryControlPointId { get; set; }
    public BaEntryControlPoint? EntryControlPoint { get; set; }

    public required string Name { get; set; }
    public required string TeamLeader { get; set; }
    public string? CommsChannel { get; set; }

    // Both optional -- a real note, not always filled in the rush of
    // getting a crew committed.
    public string? Briefing { get; set; }
    public string? Equipment { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<BaWearer> Wearers { get; set; } = [];
}

// One individual within a team -- pressure and whistle time are inherently
// personal (different cylinders, different breathing rates), so they live
// here, not on the team. Enter/Exit only, no Reopen: once a wearer's out
// that record is done; going back in is a fresh BaWearer row, matching
// real BA-board practice rather than toggling the same record back and
// forth.
public class BaWearer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TeamId { get; set; }
    public BaTeam? Team { get; set; }

    // Rank-prefixed by convention (e.g. "FF Beard"), same free-text-by-
    // convention shape as everywhere else a name is typed rather than
    // picked from a list.
    public required string Name { get; set; }
    public double CylinderPressureBar { get; set; }

    public DateTimeOffset EnteredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    // Entry time plus whatever standard duration the ECO set at the point
    // of entry -- a plain input then, not hardcoded here, since that
    // duration varies by cylinder/task/service.
    public DateTimeOffset WhistleAtUtc { get; set; }

    // Null means still in. Overdue is deliberately NOT a stored status --
    // it's ExitedAtUtc is null && now > WhistleAtUtc, computed at read
    // time (see IncidentMapping.ToDto), so it's always correct against the
    // clock rather than needing a background job to flip it.
    public DateTimeOffset? ExitedAtUtc { get; set; }
}
