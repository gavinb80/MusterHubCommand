using System.Globalization;

namespace MusterHubCommandTablet.Converters;

// A hazard timeline entry needs to read as urgent at a glance -- same
// tinted-card treatment as the web console's own hazard styling.
public class HazardToBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value as string == "Hazard" ? Color.FromArgb("#FFECEB") : Colors.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
