using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>A region frame's computed rectangle.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Frame width.</param>
/// <param name="Height">Frame height.</param>
public readonly record struct SparqlRect(double X, double Y, double Width, double Height)
{
    /// <summary>Whether <paramref name="other"/> lies entirely inside this rectangle.</summary>
    public bool Contains(SparqlRect other) =>
        other.X >= X && other.Y >= Y
        && other.X + other.Width <= X + Width
        && other.Y + other.Height <= Y + Height;
}

/// <summary>Everything the session positions: node points, and region frames with their extents.</summary>
/// <param name="NodePositions">Every node's position, authored overrides already applied.</param>
/// <param name="RegionBounds">Every region's frame, keyed by region id, anchors already applied.</param>
public sealed record SparqlLayoutResult(
    IReadOnlyDictionary<string, RegistrationPosition> NodePositions,
    IReadOnlyDictionary<string, SparqlRect> RegionBounds);

/// <summary>
/// Pure and deterministic: same projection, same picture - recursive bottom-up over the scope
/// structure, no physics. Each scope grid-places its own nodes and its child regions in
/// projection order; a parent treats a placed region as one block; <b>a region's bounds always
/// contain its contents</b>, which is a tested invariant rather than a hope. An authored node
/// position overrides its computed place; an authored region position moves the region as an
/// anchored frame, its computed contents following relatively so containment survives the move;
/// an anonymous node always takes its computed place (Requirement 5.4).
/// </summary>
public static class SparqlLayout
{
    /// <summary>
    /// A node's drawn size. Public because the viewport filter tests a node's BOX against the
    /// rectangle, not its anchor point - a node whose anchor sits just outside a viewport is
    /// still half on screen (view-delta-adoption Requirement 1.2).
    /// </summary>
    public const double NodeWidth = 170;

    /// <inheritdoc cref="NodeWidth"/>
    public const double NodeHeight = 64;
    private const double GapX = 60;
    private const double GapY = 48;
    private const double RegionPadding = 36;
    private const double RegionLabelBand = 30;
    private const double RootLeft = 40;
    private const double RootTop = 90; // below the header band
    private const int Columns = 3;

    /// <summary>Positions for <paramref name="projection"/>, with <paramref name="stored"/> authored positions applied per the rules above.</summary>
    public static SparqlLayoutResult Compute(
        SparqlProjectionResult projection,
        IReadOnlyDictionary<string, RegistrationPosition> stored)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(stored);

        // The scope structure, rebuilt from the flat lists: which nodes and which child regions
        // each scope holds directly.
        var nodesByScope = projection.Nodes
            .GroupBy(node => node.ScopePath)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var regionsByParentPath = projection.Regions
            .GroupBy(ParentPathOf)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var nodePositions = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);
        var regionBounds = new Dictionary<string, SparqlRect>(StringComparer.Ordinal);

        // Root pass: the open canvas holds the where scope's own nodes, its top-level regions,
        // and the template region beside them.
        var rootItems = ItemsOf("where", nodesByScope, regionsByParentPath, projection);
        if (regionsByParentPath.TryGetValue("", out var topLevel))
        {
            foreach (var region in topLevel.Where(region => region.ScopePath == "template"))
            {
                rootItems.Add(new Item(region.Id, MeasureRegion(region.ScopePath, nodesByScope, regionsByParentPath, projection), region));
            }
        }

        PlaceItems(rootItems, RootLeft, RootTop, nodesByScope, regionsByParentPath, projection, nodePositions, regionBounds);

        // Authored region anchors: the frame moves, its computed contents follow relatively -
        // and only computed contents, because a node's own authored position was authored
        // absolute and stays where its author put it.
        foreach (var region in projection.Regions)
        {
            if (!stored.TryGetValue(region.Id, out var anchor) || !regionBounds.TryGetValue(region.Id, out var frame))
            {
                continue;
            }

            var deltaX = anchor.X - frame.X;
            var deltaY = anchor.Y - frame.Y;
            ShiftScope(region.ScopePath, deltaX, deltaY, projection, nodePositions, regionBounds);
        }

        // Authored node positions: absolute overrides - except anonymous nodes, whose ordinals
        // are not stable identities and always take their computed place.
        foreach (var node in projection.Nodes)
        {
            if (node.Kind != SparqlNodeKind.Anonymous && stored.TryGetValue(node.Id, out var authored))
            {
                nodePositions[node.Id] = authored;
            }
        }

        return new SparqlLayoutResult(nodePositions, regionBounds);
    }

    private static string ParentPathOf(SparqlRegion region)
    {
        if (region.ParentRegionId.Length > 0)
        {
            return region.ParentRegionId["region:".Length..];
        }

        return region.ScopePath == "template" ? "" : "where";
    }

    private sealed record Item(string Id, SparqlRect Size, SparqlRegion? Region);

    private static List<Item> ItemsOf(
        string scopePath,
        Dictionary<string, List<SparqlNode>> nodesByScope,
        Dictionary<string, List<SparqlRegion>> regionsByParentPath,
        SparqlProjectionResult projection)
    {
        var items = new List<Item>();
        if (nodesByScope.TryGetValue(scopePath, out var nodes))
        {
            items.AddRange(nodes.Select(node => new Item(node.Id, new SparqlRect(0, 0, NodeWidth, NodeHeight), null)));
        }

        if (regionsByParentPath.TryGetValue(scopePath, out var regions))
        {
            items.AddRange(regions
                .Where(region => region.ScopePath != "template")
                .Select(region => new Item(
                    region.Id,
                    MeasureRegion(region.ScopePath, nodesByScope, regionsByParentPath, projection),
                    region)));
        }

        return items;
    }

    /// <summary>Bottom-up: a region's extent is its own grid's extent plus padding and its label band.</summary>
    private static SparqlRect MeasureRegion(
        string scopePath,
        Dictionary<string, List<SparqlNode>> nodesByScope,
        Dictionary<string, List<SparqlRegion>> regionsByParentPath,
        SparqlProjectionResult projection)
    {
        var items = ItemsOf(scopePath, nodesByScope, regionsByParentPath, projection);
        var (width, height) = GridExtent(items);
        return new SparqlRect(
            0,
            0,
            Math.Max(width + 2 * RegionPadding, NodeWidth),
            Math.Max(height + 2 * RegionPadding + RegionLabelBand, NodeHeight + RegionLabelBand));
    }

    private static (double Width, double Height) GridExtent(List<Item> items)
    {
        if (items.Count == 0)
        {
            return (NodeWidth, NodeHeight / 2);
        }

        double width = 0;
        double height = 0;
        double rowWidth = 0;
        double rowHeight = 0;
        var column = 0;

        foreach (var item in items)
        {
            if (column == Columns)
            {
                width = Math.Max(width, rowWidth - GapX);
                height += rowHeight + GapY;
                rowWidth = 0;
                rowHeight = 0;
                column = 0;
            }

            rowWidth += item.Size.Width + GapX;
            rowHeight = Math.Max(rowHeight, item.Size.Height);
            column++;
        }

        width = Math.Max(width, rowWidth - GapX);
        height += rowHeight;
        return (width, height);
    }

    /// <summary>Top-down: rows of up to three items, each row as tall as its tallest member.</summary>
    private static void PlaceItems(
        List<Item> items,
        double originX,
        double originY,
        Dictionary<string, List<SparqlNode>> nodesByScope,
        Dictionary<string, List<SparqlRegion>> regionsByParentPath,
        SparqlProjectionResult projection,
        Dictionary<string, RegistrationPosition> nodePositions,
        Dictionary<string, SparqlRect> regionBounds)
    {
        var x = originX;
        var y = originY;
        double rowHeight = 0;
        var column = 0;

        foreach (var item in items)
        {
            if (column == Columns)
            {
                x = originX;
                y += rowHeight + GapY;
                rowHeight = 0;
                column = 0;
            }

            if (item.Region is null)
            {
                nodePositions[item.Id] = new RegistrationPosition(x, y);
            }
            else
            {
                var frame = new SparqlRect(x, y, item.Size.Width, item.Size.Height);
                regionBounds[item.Id] = frame;
                PlaceItems(
                    ItemsOf(item.Region.ScopePath, nodesByScope, regionsByParentPath, projection),
                    x + RegionPadding,
                    y + RegionLabelBand + RegionPadding,
                    nodesByScope,
                    regionsByParentPath,
                    projection,
                    nodePositions,
                    regionBounds);
            }

            x += item.Size.Width + GapX;
            rowHeight = Math.Max(rowHeight, item.Size.Height);
            column++;
        }
    }

    /// <summary>Shifts a scope's frame and every computed position inside it - nodes placed in the scope's subtree and nested frames alike.</summary>
    private static void ShiftScope(
        string scopePath,
        double deltaX,
        double deltaY,
        SparqlProjectionResult projection,
        Dictionary<string, RegistrationPosition> nodePositions,
        Dictionary<string, SparqlRect> regionBounds)
    {
        var prefix = scopePath + "/";
        foreach (var node in projection.Nodes)
        {
            if ((node.ScopePath == scopePath || node.ScopePath.StartsWith(prefix, StringComparison.Ordinal))
                && nodePositions.TryGetValue(node.Id, out var position))
            {
                nodePositions[node.Id] = new RegistrationPosition(position.X + deltaX, position.Y + deltaY);
            }
        }

        foreach (var region in projection.Regions)
        {
            if ((region.ScopePath == scopePath || region.ScopePath.StartsWith(prefix, StringComparison.Ordinal))
                && regionBounds.TryGetValue(region.Id, out var frame))
            {
                regionBounds[region.Id] = frame with { X = frame.X + deltaX, Y = frame.Y + deltaY };
            }
        }
    }
}
