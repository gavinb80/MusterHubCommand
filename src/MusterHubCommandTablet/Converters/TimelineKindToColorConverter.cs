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
        // "Created" deliberately isn't Secondary (#0B1F3A) -- that's a
        // light-theme text/brand colour, near-black itself, so a dot or
        // caption painted with it is essentially invisible against the
        // dark theme's own near-black page background. Confirmed live on
        // the emulator. Gray400 (the same fallback used for Note/General)
        // has consistent contrast against both a white and a near-black
        // surface, which is the actual requirement for a colour used on a
        // small filled dot rather than a full-width card background.
        var key = (value as string) switch
        {
            "Created" => "Gray400",
            "ResourceChange" or "ActionChange" => "StatusMobilised",
            "Hazard" => "StatusHazard",
            "Closed" => "StatusClosed",
            _ => "Gray400", // Note, General
        };
        return Application.Current?.Resources.TryGetValue(key, out var color) == true ? color : Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
