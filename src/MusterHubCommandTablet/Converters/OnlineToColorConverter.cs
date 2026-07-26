using System.Globalization;

namespace MusterHubCommandTablet.Converters;

// Same named-resource-lookup pattern as StatusToColorConverter -- reuses
// the existing OnScene green / Open amber tokens rather than inventing new
// colours for what's really just another two-state status.
public class OnlineToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is true ? "StatusOnScene" : "StatusOpen";
        return Application.Current?.Resources.TryGetValue(key, out var color) == true ? color : Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
