using System.Globalization;

namespace MusterHubCommandTablet.Converters;

// Same named colour resources StatusToColorConverter already resolves --
// "Created" and "Closed" are synthesized entries with no IncidentUpdateType
// counterpart (see IncidentDetailViewModel.BuildTimeline), so they get
// their own two keys on top of the four real update types.
public class TimelineKindToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string) switch
        {
            "Created" => "Secondary",
            "ResourceChange" => "StatusMobilised",
            "Hazard" => "StatusHazard",
            "Closed" => "StatusClosed",
            _ => "Gray400", // Note, General
        };
        return Application.Current?.Resources.TryGetValue(key, out var color) == true ? color : Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
