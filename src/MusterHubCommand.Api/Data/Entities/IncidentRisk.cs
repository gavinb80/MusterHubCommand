namespace MusterHubCommand.Api.Data.Entities;

public enum IncidentRiskLevel
{
    Low,
    Medium,
    High,
}

public enum IncidentRiskStatus
{
    Identified,
    Controlled,
}

// A structured Dynamic Risk Assessment log, distinct from a plain free-text
// Hazard timeline entry -- risk level, control measure and a review stamp
// each need their own place, not just a line in the general feed. Unlike
// IncidentObjective, raising a risk DOES write a Timeline entry (see
// IncidentService.RaiseRiskAsync) -- a new hazard is a real event worth
// broadcasting. Only the ongoing Identified<->Controlled state stays out of
// the Timeline, same reasoning Objectives already established for its own
// Achieve/Reopen.
public class IncidentRisk
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public required string Description { get; set; }
    public IncidentRiskLevel RiskLevel { get; set; }
    public string? ControlMeasure { get; set; }
    public IncidentRiskStatus Status { get; set; } = IncidentRiskStatus.Identified;

    public IncidentUpdateSource Source { get; set; }

    // Always resolved server-side from the caller's own session, same rule
    // as IncidentObjective.RaisedByName.
    public required string RaisedByName { get; set; }
    public Guid? RaisedByEmployeeId { get; set; }
    public Employee? RaisedByEmployee { get; set; }

    // Cleared, not left alone, when ReopenRiskAsync flips Status back to
    // Identified -- so a later re-Control stamps a fresh time/name, not the
    // original reviewer's. Same shape as IncidentObjective.AchievedAtUtc.
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewedByName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
