using System.Globalization;

namespace MusterHubCommandTablet.Converters;

// For a connector-line segment that must keep its layout slot even when
// not drawn -- IsVisible=False collapses space in a StackLayout and would
// shift every column after it out of alignment, so the guide/elbow lines
// in IncidentHierarchyPage hide via Opacity instead.
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1d : 0d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
