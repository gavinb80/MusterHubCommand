using System.Globalization;

namespace MusterHubCommandTablet.Converters;

// Generic "is this nullable value actually set" visibility gate -- unlike
// StringNotEmptyToBoolConverter (string-specific: empty vs. whitespace vs.
// null all collapse to the same false), this is for a nullable struct
// (DateTimeOffset?, double?) where the only meaningful states are
// null/not-null.
public class ObjectNotNullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
