using MusterHubCommandTablet.Models;

namespace MusterHubCommandTablet.ViewModels;

// X/Y are the node's own center-top point (what an Edge connects to).
// LayoutBounds is the box's absolute placement rect for
// AbsoluteLayout.LayoutBounds (left edge, not center -- the two need to
// differ so a box's rendered position stays centered on the same point
// its connector lines terminate at).
public record PositionedAppliance(IncidentApplianceDto Appliance, double X, double Y)
{
    public Rect LayoutBounds => new(X - HierarchyLayout.NodeBoxWidth / 2, Y, HierarchyLayout.NodeBoxWidth, HierarchyLayout.ApplianceBoxHeight);
}

public record PositionedNode(
    IncidentSectorDto Node, double X, double Y, bool HasChildren, bool IsExpanded,
    List<PositionedAppliance> Appliances)
{
    public Rect LayoutBounds => new(X - HierarchyLayout.NodeBoxWidth / 2, Y, HierarchyLayout.NodeBoxWidth, HierarchyLayout.NodeBoxHeight);
}

// A parent-to-child (or node-to-appliance) connector, already in pixel
// coordinates. Drawn as a stem down from (X1,Y1) to MidY, across to X2,
// then down to (X2,Y2) -- when several children share the same parent,
// their edges share the same X1/Y1/MidY, so drawing each independently
// still composes into one shared stem forking into a horizontal bar, the
// same look the wireframe uses. Appliance edges have X1 == X2, which
// collapses the same path into a plain vertical line.
public record Edge(double X1, double Y1, double X2, double Y2, double MidY);

public record HierarchyLayoutResult(List<PositionedNode> Nodes, List<Edge> Edges, double CanvasWidth, double CanvasHeight);

// Classic subtree-centering tree layout: a width pass computes each
// node's horizontal footprint bottom-up (in unit slots), then a position
// pass assigns actual pixel X/Y top-down using those widths, centering
// each node over the span its own subtree occupies. Appliances are
// leaves -- they stack straight down beneath whatever node they're
// attached to rather than branching sideways -- so they never factor
// into the width pass, only into how tall that one column's content is.
// Generalizes to any width/depth; nothing here assumes a fixed number of
// levels or a maximum fan-out.
public static class HierarchyLayout
{
    public const double UnitWidth = 240;
    public const double NodeBoxWidth = 210;
    public const double NodeBoxHeight = 84;
    public const double ApplianceBoxHeight = 52;
    public const double RowHeight = 108;
    public const double ApplianceRowHeight = 66;
    private const double TopMargin = 24;
    private const double SideMargin = 100;

    public static HierarchyLayoutResult Build(IncidentDto incident, Dictionary<Guid, bool> expandedState)
    {
        var appliancesByNode = incident.Appliances
            .Where(a => a.SectorId is not null)
            .GroupBy(a => a.SectorId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Guid.Empty as the "no parent" sentinel, not null -- Dictionary's
        // TKey has a notnull constraint, and no real IncidentSector.Id is
        // ever Guid.Empty (they're all server-generated via Guid.NewGuid).
        var byParent = incident.Sectors
            .GroupBy(s => s.ParentId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.SortOrder).ToList());

        var widths = new Dictionary<Guid, double>();
        double Width(Guid nodeId)
        {
            if (widths.TryGetValue(nodeId, out var cached)) return cached;
            var expanded = expandedState.GetValueOrDefault(nodeId, true);
            var w = 1d;
            if (expanded && byParent.TryGetValue(nodeId, out var children) && children.Count > 0)
                w = children.Sum(c => Width(c.Id));
            widths[nodeId] = w;
            return w;
        }
        foreach (var siblingGroup in byParent.Values)
            foreach (var node in siblingGroup)
                Width(node.Id);

        var nodes = new List<PositionedNode>();
        var edges = new List<Edge>();
        double maxX = 0, maxY = 0;

        // parentBottomVisual/nodeBottomVisual track the actual bottom
        // edge of whatever box precedes the next thing drawn (the visual
        // bottom, not the row-slot boundary -- RowHeight/ApplianceRowHeight
        // are slot-to-slot spacing, taller than the boxes themselves so a
        // gap shows between rows, so an edge must start at the shorter
        // box height or every connector ends up zero-length).
        void Assign(IncidentSectorDto node, double unitStart, double unitEnd, double topY, double? parentCenterX, double? parentBottomVisual)
        {
            var centerX = (unitStart + unitEnd) / 2 * UnitWidth + SideMargin;
            var hasChildren = byParent.ContainsKey(node.Id);
            var isExpanded = expandedState.GetValueOrDefault(node.Id, true);

            if (parentCenterX is not null)
                edges.Add(new Edge(parentCenterX.Value, parentBottomVisual!.Value, centerX, topY, (parentBottomVisual.Value + topY) / 2));

            var positionedAppliances = new List<PositionedAppliance>();
            var slotY = topY + RowHeight;
            var bottomVisual = topY + NodeBoxHeight;
            foreach (var appliance in appliancesByNode.GetValueOrDefault(node.Id, []))
            {
                positionedAppliances.Add(new PositionedAppliance(appliance, centerX, slotY));
                edges.Add(new Edge(centerX, bottomVisual, centerX, slotY, (bottomVisual + slotY) / 2));
                bottomVisual = slotY + ApplianceBoxHeight;
                slotY += ApplianceRowHeight;
            }

            nodes.Add(new PositionedNode(node, centerX, topY, hasChildren, isExpanded, positionedAppliances));

            maxX = Math.Max(maxX, centerX);
            maxY = Math.Max(maxY, bottomVisual);

            if (!isExpanded || !byParent.TryGetValue(node.Id, out var children) || children.Count == 0) return;

            var cursor = unitStart;
            foreach (var child in children)
            {
                var childWidth = widths[child.Id];
                Assign(child, cursor, cursor + childWidth, slotY, centerX, bottomVisual);
                cursor += childWidth;
            }
        }

        var roots = byParent.GetValueOrDefault(Guid.Empty, []);
        var rootCursor = 0d;
        foreach (var root in roots)
        {
            var rootWidth = widths[root.Id];
            Assign(root, rootCursor, rootCursor + rootWidth, TopMargin, null, null);
            rootCursor += rootWidth;
        }

        return new HierarchyLayoutResult(nodes, edges, maxX + SideMargin, maxY + TopMargin);
    }
}
