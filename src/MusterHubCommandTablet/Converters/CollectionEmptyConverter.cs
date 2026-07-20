using System.Collections;
using System.Globalization;

namespace MusterHubCommandTablet.Converters;

public class CollectionEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ICollection { Count: 0 } or null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
