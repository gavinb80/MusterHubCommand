using System.Globalization;

namespace MusterHubCommandTablet.Converters;

// Covers both Incident.Status and IncidentAppliance.Status: the two
// vocabularies don't overlap ("Open"/"Closed"/"Cancelled" vs.
// "Mobilised"/"EnRoute"/"OnScene"/"StoodDown"), so one converter and one
// lookup table serves both status pills without needing to know which kind
// of status it's colouring.
public class StatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string) switch
        {
            "Open" => "StatusOpen",
            "Closed" or "Cancelled" => "StatusClosed",
            "Mobilised" => "StatusMobilised",
            "EnRoute" => "StatusEnRoute",
            "OnScene" => "StatusOnScene",
            "StoodDown" => "StatusStoodDown",
            _ => "Gray400",
        };
        return Application.Current?.Resources.TryGetValue(key, out var color) == true ? color : Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
