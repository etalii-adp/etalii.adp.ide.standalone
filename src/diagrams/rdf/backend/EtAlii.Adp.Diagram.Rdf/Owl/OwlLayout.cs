using EtAlii.Adp.Layout;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The ontology reading's computed layout: the asserted class tree as geometry (owl-diagram
/// Requirement 2.1). Classes fall into columns by asserted subclass depth - roots left, leaves
/// right, a cycle collapsed to one column - so the graph reads as the tree a Protégé user
/// expects; expression nodes, datatype targets and Thing anchors stack in a lane beside the node
/// whose structure attaches them; individuals band below the TBox; the header sits top-left.
/// A pure function, no physics, no randomness.
/// </summary>
/// <remarks>
/// <para>
/// Three rules keep a real ontology readable rather than a thicket, all found by drawing OWL-Time
/// (114 elements, 55 of them expressions) and looking at it:
/// </para>
/// <para>
/// <b>A class owns a slot, not a row.</b> Its height is whatever its satellites need, so a class
/// with nine restrictions pushes the next class down instead of overlapping it.
/// </para>
/// <para>
/// <b>Satellites live in their own lane</b> between the class columns, and the column pitch is
/// wide enough for the class, the lane and a gap. Placing them at a fixed small offset put them
/// on top of the next column.
/// </para>
/// <para>
/// <b>Classes are ordered within a column by where their parents sit</b> - the barycentre of the
/// rows they descend from, ties broken by document order. It is one deterministic pass, not a
/// simulation, and it is what stops subclass edges from crossing the whole canvas.
/// </para>
/// </remarks>
internal static class OwlLayout
{
    /// <summary>A class or expression's drawn size, and a Thing anchor's smaller one - what the canvas draws (owl-diagram design).</summary>
    private const double ShapeWidth = 190;
    private const double ShapeHeight = 70;
    private const double ThingScale = 0.55;

    /// <summary>A card's drawn width, and the parts its height is made of.</summary>
    private const double CardWidth = 220;
    private const double CardHeaderHeight = 30;
    private const double CardBadgesHeight = 16;
    private const double CardRowHeight = 16;
    private const double CardFooterPadding = 10;

    /// <summary>The pitch between class columns: the class, the satellite lane beside it, and air.</summary>
    private const double LaneOffsetX = 250;
    private const double LaneWidth = ShapeWidth;
    private const double ColumnGap = 90;
    private const double ColumnWidth = LaneOffsetX + LaneWidth + ColumnGap;

    /// <summary>The vertical rhythm: the least a class slot occupies, and what one satellite adds.</summary>
    private const double SlotHeight = 130;
    private const double SatelliteHeight = 110;
    private const double SatelliteFirstOffsetY = 10;

    /// <summary>The air under the header card and above a band - added to the real heights, never instead of them.</summary>
    private const double HeaderGap = 80;
    private const double BandGap = 140;
    private const double BandColumnGap = 60;
    private const double BandRowGap = 50;
    private const int BandColumns = 5;

    /// <summary>A position for every drawn node of <paramref name="graph"/>.</summary>
    public static IReadOnlyDictionary<string, RegistrationPosition> Positions(OwlGraphResult graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var positions = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

        var headerHeight = PlaceHeader(graph, positions);

        var order = graph.Nodes.Select((node, index) => (node.Id, index))
            .ToDictionary(entry => entry.Id, entry => entry.index, StringComparer.Ordinal);

        // The class columns: asserted subclass depth over the drawn subclass edges between
        // class-like nodes, cycles collapsed by the shared hierarchy walk.
        var classIds = graph.Nodes
            .Where(node => node.Kind is OwlNodeKind.Class or OwlNodeKind.Thing && node.Id.StartsWith("res:", StringComparison.Ordinal))
            .Select(node => node.Id)
            .ToList();
        var classSet = classIds.ToHashSet(StringComparer.Ordinal);
        var supers = Supers(graph, classIds, classSet);

        var depths = OwlClassHierarchy.Depths(supers);

        var satellites = Satellites(graph, classIds, classSet, positions);

        var columns = Columns(classIds, depths);
        var maxY = PlaceClasses(columns, supers, satellites, order, positions, headerHeight);

        // Individuals band below the TBox, then whatever still has no place - orphaned anchors,
        // free-floating nodes - in a final band; both grid-placed in document order.
        maxY = Band(graph.Nodes.Where(node => node.Kind == OwlNodeKind.Individual && !positions.ContainsKey(node.Id)), maxY, positions);
        Band(graph.Nodes.Where(node => !positions.ContainsKey(node.Id)), maxY, positions);

        return positions;
    }

    /// <summary>
    /// Places the header, top-left, before everything (Requirement 1.5), and returns where the
    /// classes start.
    /// </summary>
    private static double PlaceHeader(OwlGraphResult graph, Dictionary<string, RegistrationPosition> positions)
    {
        // The classes start below ITS OWN height: a header carrying a dozen annotation rows is a
        // tall card, and a fixed gap put the first class straight through it.
        var header = graph.Nodes.FirstOrDefault(node => node.Kind == OwlNodeKind.OntologyHeader);
        var headerHeight = 0d;
        if (header is not null)
        {
            positions[header.Id] = new RegistrationPosition(0, 0);
            headerHeight = SizeOf(header).Height + HeaderGap;
        }

        return headerHeight;
    }

    /// <summary>Each class's asserted superclasses, over the drawn subclass edges between class-like nodes.</summary>
    private static Dictionary<string, List<string>> Supers(OwlGraphResult graph, List<string> classIds, HashSet<string> classSet)
    {
        var supers = classIds.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (edge.Kind == OwlEdgeKind.Subclass && classSet.Contains(edge.FromId) && classSet.Contains(edge.ToId))
            {
                supers[edge.FromId].Add(edge.ToId);
            }
        }

        return supers;
    }

    /// <summary>What hangs off each class: its expressions, and the datatypes and anchors only it reaches.</summary>
    private static Dictionary<string, List<string>> Satellites(
        OwlGraphResult graph,
        List<string> classIds,
        HashSet<string> classSet,
        Dictionary<string, RegistrationPosition> positions)
    {
        // Counted first, because a class's slot is as tall as its satellites need.
        var satellites = classIds.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            if (positions.ContainsKey(node.Id) || classSet.Contains(node.Id) || node.Kind == OwlNodeKind.Individual)
            {
                continue;
            }

            var host = node.OwnerId.Length > 0 && satellites.ContainsKey(node.OwnerId)
                ? node.OwnerId
                : graph.Edges
                    .Where(edge => edge.FromId == node.Id || edge.ToId == node.Id)
                    .Select(edge => edge.FromId == node.Id ? edge.ToId : edge.FromId)
                    .FirstOrDefault(satellites.ContainsKey);
            if (host is not null)
            {
                satellites[host].Add(node.Id);
            }
        }

        return satellites;
    }

    /// <summary>The classes grouped into columns by depth, in document order within a column.</summary>
    private static SortedDictionary<int, List<string>> Columns(List<string> classIds, Dictionary<string, int> depths)
    {
        var columns = new SortedDictionary<int, List<string>>();
        foreach (var id in classIds)
        {
            var depth = depths.TryGetValue(id, out var d) ? d : 0;
            if (!columns.TryGetValue(depth, out var members))
            {
                members = [];
                columns[depth] = members;
            }

            members.Add(id);
        }

        return columns;
    }

    /// <summary>
    /// Places every class and its satellites, column by column, and returns the lowest edge reached.
    /// </summary>
    private static double PlaceClasses(
        SortedDictionary<int, List<string>> columns,
        Dictionary<string, List<string>> supers,
        Dictionary<string, List<string>> satellites,
        Dictionary<string, int> order,
        Dictionary<string, RegistrationPosition> positions,
        double classTop)
    {
        // Column order: parents first, then each column arranged by the barycentre of the rows
        // its members descend from - the one pass that stops the hierarchy edges from crossing.
        var rowOf = new Dictionary<string, double>(StringComparer.Ordinal);
        var maxY = classTop;
        foreach ((int depth, List<string> members) in columns)
        {
            var arranged = Arrange(depth, members, supers, order, rowOf);

            var y = classTop;
            foreach (var id in arranged)
            {
                var attached = satellites[id];
                positions[id] = new RegistrationPosition(depth * ColumnWidth, y);
                rowOf[id] = y;

                // The satellites, stacked in the lane beside their class.
                for (var i = 0; i < attached.Count; i++)
                {
                    positions[attached[i]] = new RegistrationPosition(
                        depth * ColumnWidth + LaneOffsetX,
                        y + SatelliteFirstOffsetY + i * SatelliteHeight);
                }

                var slot = Math.Max(SlotHeight, attached.Count * SatelliteHeight + SlotHeight / 2);
                y += slot;
                maxY = Math.Max(maxY, y);
            }
        }

        return maxY;
    }

    /// <summary>A column's members in drawing order: by document order at the root, by the barycentre of their parents' rows below it.</summary>
    private static List<string> Arrange(
        int depth,
        List<string> members,
        Dictionary<string, List<string>> supers,
        Dictionary<string, int> order,
        Dictionary<string, double> rowOf) =>
        depth == 0
            ? members.OrderBy(id => order[id]).ToList()
            : members
                .OrderBy(id =>
                {
                    var parents = supers[id].Where(rowOf.ContainsKey).Select(parent => rowOf[parent]).ToList();
                    return parents.Count > 0 ? parents.Average() : double.MaxValue;
                })
                .ThenBy(id => order[id])
                .ToList();

    /// <summary>Grid-places <paramref name="nodes"/> in a band below <paramref name="top"/>, and returns the band's bottom.</summary>
    private static double Band(IEnumerable<OwlNode> nodes, double top, Dictionary<string, RegistrationPosition> positions)
    {
        // A band's pitch comes from what it actually holds: individual cards grow a row per
        // literal assertion, and a fixed row height drew them through each other.
        var banded = nodes.ToList();
        if (banded.Count == 0)
        {
            return top;
        }

        var columnPitch = banded.Max(node => SizeOf(node).Width) + BandColumnGap;
        var rowPitch = banded.Max(node => SizeOf(node).Height) + BandRowGap;
        var y = top + BandGap;
        var bottom = top;
        for (var placed = 0; placed < banded.Count; placed++)
        {
            (double x, double down) = GridPacker.Cell(placed, BandColumns, columnPitch, rowPitch);
            var position = new RegistrationPosition(x, y + down);
            positions[banded[placed].Id] = position;
            bottom = Math.Max(bottom, position.Y + rowPitch);
        }

        return bottom;
    }

    /// <summary>
    /// What a node occupies, in the module's own units - the sizes the canvas draws it at. The
    /// layout has to know them to keep elements apart, and the overlap guard measures with them.
    /// </summary>
    public static (double Width, double Height) SizeOf(OwlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Kind switch
        {
            OwlNodeKind.Individual or OwlNodeKind.OntologyHeader => (
                CardWidth,
                CardHeaderHeight
                + (node.Badges.Count > 0 ? CardBadgesHeight : 0)
                + (node.Rows.Count * CardRowHeight)
                + CardFooterPadding),
            OwlNodeKind.Thing => (ShapeWidth * ThingScale, ShapeHeight * ThingScale),
            _ => (ShapeWidth, ShapeHeight),
        };
    }

    /// <summary>
    /// The overlay, under this reading's boundary: a stored position wins for an IRI-derived id
    /// (<c>res:</c>, <c>ind:</c>), and never applies to an expression node or a materialized
    /// anchor - their ids do not survive an edit, which is the blank-node identity boundary's
    /// whole point (Requirement 3.2).
    /// </summary>
    public static IReadOnlyDictionary<string, RegistrationPosition> Apply(
        IReadOnlyDictionary<string, RegistrationPosition> computed,
        IReadOnlyDictionary<string, RegistrationPosition> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var positionable = stored
            .Where(entry => IsPositionable(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        return RegistrationLayout.Apply(computed, positionable);
    }

    /// <summary>Whether an element of this reading may take a stored position at all.</summary>
    public static bool IsPositionable(string elementId) =>
        elementId.StartsWith("res:", StringComparison.Ordinal)
        || elementId.StartsWith("ind:", StringComparison.Ordinal);
}
