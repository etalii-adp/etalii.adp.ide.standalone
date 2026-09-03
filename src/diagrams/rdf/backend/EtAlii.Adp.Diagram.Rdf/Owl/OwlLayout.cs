using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The ontology reading's computed layout: the asserted class tree as geometry (owl-diagram
/// Requirement 2.1). Classes fall into columns by asserted subclass depth - roots left, leaves
/// right, a cycle collapsed to one column - so the graph reads as the tree a Protégé user
/// expects; expression nodes, datatype targets and Thing anchors stack as satellites beside the
/// node their structure attaches to; individuals band below the TBox; the header sits top-left.
/// A pure function, no physics, no randomness.
/// </summary>
internal static class OwlLayout
{
    private const double ColumnWidth = 300;
    private const double RowHeight = 130;
    private const double HeaderGap = 150;
    private const double SatelliteOffsetX = 170;
    private const double SatelliteOffsetY = 70;
    private const double SatelliteRowHeight = 80;
    private const double BandGap = 160;
    private const int BandColumns = 4;

    /// <summary>A position for every drawn node of <paramref name="graph"/>.</summary>
    public static IReadOnlyDictionary<string, RegistrationPosition> Positions(OwlGraphResult graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var positions = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

        // The header, top-left, before everything (Requirement 1.5).
        var header = graph.Nodes.FirstOrDefault(node => node.Kind == OwlNodeKind.OntologyHeader);
        if (header is not null)
        {
            positions[header.Id] = new RegistrationPosition(0, 0);
        }

        // The class columns: asserted subclass depth over the drawn subclass edges between
        // class-like nodes, cycles collapsed by the shared hierarchy walk.
        var classIds = graph.Nodes
            .Where(node => node.Kind is OwlNodeKind.Class or OwlNodeKind.Thing && node.Id.StartsWith("res:", StringComparison.Ordinal))
            .Select(node => node.Id)
            .ToList();
        var classSet = classIds.ToHashSet(StringComparer.Ordinal);
        var supers = classIds.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (edge.Kind == OwlEdgeKind.Subclass && classSet.Contains(edge.FromId) && classSet.Contains(edge.ToId))
            {
                supers[edge.FromId].Add(edge.ToId);
            }
        }

        var depths = OwlClassHierarchy.Depths(supers);
        var rowPerColumn = new Dictionary<int, int>();
        var classTop = header is null ? 0d : HeaderGap;
        var maxY = classTop;
        foreach (var id in classIds)
        {
            var depth = depths.TryGetValue(id, out var d) ? d : 0;
            rowPerColumn.TryGetValue(depth, out var row);
            rowPerColumn[depth] = row + 1;
            var position = new RegistrationPosition(depth * ColumnWidth, classTop + row * RowHeight);
            positions[id] = position;
            maxY = Math.Max(maxY, position.Y);
        }

        // Satellites: expression nodes beside their owner; a datatype or Thing anchor beside the
        // first placed node an edge ties it to. Stacked per host in document order.
        var satelliteCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            var host = node.OwnerId.Length > 0 && positions.ContainsKey(node.OwnerId) ? node.OwnerId : null;
            if (host is null && node.Kind is OwlNodeKind.Datatype or OwlNodeKind.Thing && !positions.ContainsKey(node.Id))
            {
                host = graph.Edges
                    .Where(edge => edge.FromId == node.Id || edge.ToId == node.Id)
                    .Select(edge => edge.FromId == node.Id ? edge.ToId : edge.FromId)
                    .FirstOrDefault(positions.ContainsKey);
            }

            if (host is null || positions.ContainsKey(node.Id))
            {
                continue;
            }

            satelliteCounts.TryGetValue(host, out var stacked);
            satelliteCounts[host] = stacked + 1;
            var anchor = positions[host];
            var position = new RegistrationPosition(
                anchor.X + SatelliteOffsetX,
                anchor.Y + SatelliteOffsetY + stacked * SatelliteRowHeight);
            positions[node.Id] = position;
            maxY = Math.Max(maxY, position.Y);
        }

        // Individuals band below the TBox, then whatever still has no place - orphaned anchors,
        // free-floating nodes - in a final band; both grid-placed in document order.
        maxY = Band(graph.Nodes.Where(node => node.Kind == OwlNodeKind.Individual && !positions.ContainsKey(node.Id)), maxY);
        Band(graph.Nodes.Where(node => !positions.ContainsKey(node.Id)), maxY);

        return positions;

        double Band(IEnumerable<OwlNode> nodes, double top)
        {
            var y = top + BandGap;
            var placed = 0;
            var bottom = top;
            foreach (var node in nodes)
            {
                var position = new RegistrationPosition(
                    placed % BandColumns * ColumnWidth,
                    y + placed / BandColumns * RowHeight);
                positions[node.Id] = position;
                bottom = Math.Max(bottom, position.Y);
                placed++;
            }

            return bottom;
        }
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
