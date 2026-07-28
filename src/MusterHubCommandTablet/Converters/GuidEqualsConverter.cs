using System.Globalization;

namespace MusterHubCommandTablet.Converters;

// Compares a per-item id (one MultiBinding value, from the item's own
// DataTemplate context) against a ViewModel-level "which one is open" id
// (the other value, from Source={x:Reference PageRoot}) -- lets a nested
// list toggle a single item's own inline form without needing a matching
// bool per item in the ViewModel.
public class GuidEqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [Guid itemId, ..]) return false;
        return values[1] is Guid openId && openId == itemId;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
