using System.Globalization;

namespace MusterHubCommandTablet.Converters;

// Solid triangle glyphs, not an icon font -- this app has no icon font
// dependency elsewhere (see the back button's own &#8592; escape), so a
// plain Unicode character is the same idiom, not a new one.
public class ExpandGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "▾" : "▸";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
