using System.Globalization;

namespace MusterHubCommandTablet.Converters;

// Display text for IncidentAction.Kind -- same short-label idea as the
// web console's ACTION_KIND_LABELS map, kept in sync by hand.
public class IncidentActionKindLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "ResourceRequest" => "Resource Request",
            "Task" => "Task",
            _ => "",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
