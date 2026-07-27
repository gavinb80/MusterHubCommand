using System.Globalization;

namespace MusterHubCommandTablet.Converters;

// Covers Incident.Status, IncidentAppliance.Status, and IncidentAction.Status:
// the vocabularies don't overlap ("Open"/"Closed"/"Cancelled" vs.
// "Mobilised"/"EnRoute"/"OnScene"/"StoodDown" vs.
// "Open"/"Acknowledged"/"Completed"/"Declined" -- Open itself is shared
// between the first and third and maps consistently either way), so one
// converter and one lookup table serves every status pill without needing
// to know which kind of status it's colouring.
public class StatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string) switch
        {
            "Open" => "StatusOpen",
            "Closed" or "Cancelled" => "StatusClosed",
            "Mobilised" => "StatusMobilised",
            "EnRoute" or "Acknowledged" => "StatusEnRoute",
            "OnScene" or "Completed" => "StatusOnScene",
            "StoodDown" => "StatusStoodDown",
            "Declined" => "StatusHazard",
            _ => "Gray400",
        };
        return Application.Current?.Resources.TryGetValue(key, out var color) == true ? color : Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
