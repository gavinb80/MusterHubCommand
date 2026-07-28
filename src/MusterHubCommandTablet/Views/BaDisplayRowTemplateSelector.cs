using MusterHubCommandTablet.ViewModels;

namespace MusterHubCommandTablet.Views;

// A real selector, not one shared DataTemplate with IsVisible toggles --
// the latter left hidden siblings' measured space behind inside a recycled
// CollectionView cell (the grey dead-space bug), because MAUI doesn't
// always re-measure a cell down when a DataTrigger flips something to
// collapsed. Each Kind gets its own template here, so a cell only ever
// contains the one panel it actually needs.
public class BaDisplayRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TeamHeaderTemplate { get; set; }
    public DataTemplate? AddWearerFormTemplate { get; set; }
    public DataTemplate? NoWearersTemplate { get; set; }
    public DataTemplate? WearerTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
        ((BaDisplayRow)item).Kind switch
        {
            "TeamHeader" => TeamHeaderTemplate!,
            "AddWearerForm" => AddWearerFormTemplate!,
            "NoWearers" => NoWearersTemplate!,
            "Wearer" => WearerTemplate!,
            var kind => throw new InvalidOperationException($"Unknown BaDisplayRow.Kind '{kind}'."),
        };
}
