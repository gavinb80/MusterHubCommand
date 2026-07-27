using System.Globalization;

namespace MusterHubCommandTablet.Converters;

// Display text for IncidentAppliance.ResourceKind -- same short-label idea
// as the web console's RESOURCE_KIND_LABELS map, kept in sync by hand
// rather than shared, same as this app's own timeline-merge logic already
// is with the web console's buildTimeline.
public class ResourceKindLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "OfficerVehicle" => "Officer",
            "Specialist" => "Specialist",
            _ => "",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
