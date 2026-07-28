using System.Globalization;

namespace MusterHubCommandTablet.Converters;

// Covers Incident.Status, IncidentAppliance.Status, IncidentAction.Status,
// IncidentObjective.Status, IncidentRisk.Status, IncidentRisk.RiskLevel,
// BaEntry.Status and IncidentDetailViewModel.BaDisplayRow.Urgency: the
// vocabularies don't overlap ("Open"/"Closed"/"Cancelled" vs. "Mobilised"/
// "EnRoute"/"OnScene"/"StoodDown" vs. "Open"/"Acknowledged"/"Completed"/
// "Declined" vs. "Open"/"Achieved" vs. "Identified"/"Controlled" vs. "Low"/
// "Medium"/"High" vs. "InBa"/"Exited"/"Overdue"/"Warning" -- Open itself is
// shared across several and maps consistently either way), so one
// converter and one lookup table serves every status pill without needing
// to know which kind of status it's colouring. RiskLevel/BaEntry both
// reuse the same existing tokens the web console's own RISK_LEVEL_STYLES/
// BA_ENTRY_STATUS_STYLES do, not new colours. Warning (a team's own
// "someone's due out soon" state) reuses the same amber as EnRoute/Medium,
// not a colour of its own.
public class StatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string) switch
        {
            "Open" => "StatusOpen",
            "Closed" or "Cancelled" => "StatusClosed",
            "Mobilised" or "InBa" => "StatusMobilised",
            "EnRoute" or "Acknowledged" => "StatusEnRoute",
            "OnScene" or "Completed" or "Achieved" or "Controlled" or "Low" or "Exited" => "StatusOnScene",
            "StoodDown" => "StatusStoodDown",
            "Declined" or "Identified" or "High" or "Overdue" => "StatusHazard",
            "Medium" or "Warning" => "StatusOpen",
            _ => "Gray400",
        };
        return Application.Current?.Resources.TryGetValue(key, out var color) == true ? color : Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
