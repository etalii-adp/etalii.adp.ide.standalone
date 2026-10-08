using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The tree-shaped layout (skos-diagram Requirement 4): schemes as stacked regions in IRI order,
/// top concepts on a region's first row, every concept one layer below its deepest placed broader
/// concept, polyhierarchy placed once, unfiled concepts in a final band, collections in a
/// trailing band of their own. Pure and deterministic - no physics, no randomness; the layout
/// overlay merges authored positions on top, element by element.
/// </summary>
/// <remarks>
/// A layer wider than its band's column budget wraps onto further rows, and every row is centred
/// on the band. A real thesaurus is a few layers deep and hundreds of concepts wide: with one row
/// per layer the STW geographic names came out 166 concepts wide and seven rows tall, which fitted
/// to a window reads as one horizontal line. The budget grows with the square root of the band's
/// size, so a band stays roughly as wide as it is tall however large it gets. Within a layer,
/// concepts sit in the order of their broader concepts, so siblings stay together when a layer
/// wraps and the hierarchy edges run down rather than across the whole band.
/// </remarks>
/// <remarks>
/// Cycles are one detection here and nowhere else: layering runs on an acyclic subset obtained
/// by repeatedly excluding, per remaining knot, the edge whose (broader IRI, narrower IRI) pair
/// sorts lowest - excluded from layering only, still drawn - and the validator consumes
/// <see cref="SkosLayoutResult.Cycles"/> rather than detecting again, so the picture and the
/// problem report cannot disagree (Requirements 4.2, 7.1).
/// </remarks>
public static class SkosLayout
{
    private const double ColumnWidth = 240;
    private const double RowHeight = 110;
    private const double RegionGap = 90;
    private const double RegionHeader = 60;
    private const double LayerGap = 50;
    private const int MinimumColumns = 8;

    /// <summary>Positions for every drawn element, and the cycles found on the way.</summary>
    public static SkosLayoutResult Layout(SkosProjectionResult projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var hierarchy = projection.Edges.Where(edge => edge.Kind == SkosEdgeKind.Hierarchy).ToList();
        (IReadOnlyList<SkosEdge> acyclic, IReadOnlyList<SkosCycle> cycles) = BreakCycles(hierarchy);

        var childrenOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var broadersOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in acyclic)
        {
            AddTo(childrenOf, edge.FromId, edge.ToId);
            AddTo(broadersOf, edge.ToId, edge.FromId);
        }

        var positions = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var y = 0d;

        foreach (var scheme in projection.Schemes.OrderBy(s => s.Iri, StringComparer.Ordinal))
        {
            positions[scheme.Id] = new RegistrationPosition(0, y);
            var members = projection.Concepts
                .Where(concept => concept.SchemeIris.Contains(scheme.Iri) && !placed.Contains(concept.Id))
                .Select(concept => concept.Id)
                .ToHashSet(StringComparer.Ordinal);
            y = PlaceBand(members, y + RegionHeader) + RegionGap;
        }

        var unfiled = projection.Concepts.Where(concept => !placed.Contains(concept.Id)).Select(concept => concept.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (unfiled.Count > 0)
        {
            y = PlaceBand(unfiled, y) + RegionGap;
        }

        var collections = projection.Collections.OrderBy(c => c.Iri, StringComparer.Ordinal).ToList();
        var collectionColumns = ColumnsFor(collections.Count);
        for (var index = 0; index < collections.Count; index++)
        {
            // A grid filled row by row: the whole row and column of the index, never a fraction.
            (int row, int column) = Math.DivRem(index, collectionColumns);
            positions[collections[index].Id] = new RegistrationPosition(column * ColumnWidth, y + (row * RowHeight));
        }

        return new SkosLayoutResult(positions, cycles);

        // Layers a band's members: layer 0 is everyone with no placed broader inside the band,
        // then longest-path depth below; a member every path missed still lands, by id, at the
        // bottom. Returns the y just past the band.
        double PlaceBand(HashSet<string> members, double top)
        {
            var depth = new Dictionary<string, int>(StringComparer.Ordinal);
            var layer = members
                .Where(id => !(broadersOf.TryGetValue(id, out var b) && b.Any(members.Contains)))
                .Order(StringComparer.Ordinal)
                .ToList();
            var current = 0;
            while (layer.Count > 0)
            {
                foreach (var id in layer)
                {
                    depth[id] = current; // The deepest broader wins: later, deeper visits overwrite.
                }

                layer = layer
                    .SelectMany(id => childrenOf.TryGetValue(id, out var children) ? children : [])
                    .Where(id => members.Contains(id) && (!depth.TryGetValue(id, out var d) || d < current + 1))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToList();
                current++;
                if (current > members.Count)
                {
                    break; // A knot the exclusion did not fully unwind; the remainder lands below.
                }
            }

            var layers = depth
                .GroupBy(pair => pair.Value)
                .OrderBy(group => group.Key)
                .Select(group => group.Select(pair => pair.Key).ToList())
                .ToList();
            var leftover = members.Where(id => !depth.ContainsKey(id)).Order(StringComparer.Ordinal).ToList();
            if (leftover.Count > 0)
            {
                layers.Add(leftover);
            }

            // Each layer in the order of its broader concepts already placed, so siblings sit
            // together; a concept with no placed broader (layer 0, the leftovers) keeps id order.
            var sequence = new Dictionary<string, int>(StringComparer.Ordinal);
            var ordered = new List<List<string>>();
            foreach (var level in layers)
            {
                var sorted = level
                    .OrderBy(id => BroaderPosition(id, sequence))
                    .ThenBy(id => id, StringComparer.Ordinal)
                    .ToList();
                foreach (var id in sorted)
                {
                    sequence[id] = sequence.Count;
                }

                ordered.Add(sorted);
            }

            var columns = ColumnsFor(members.Count);
            var bandColumns = ordered.Count == 0 ? 0 : Math.Min(columns, ordered.Max(level => level.Count));
            var rowTop = top;
            foreach (var level in ordered)
            {
                for (var start = 0; start < level.Count; start += columns)
                {
                    var row = level.Skip(start).Take(columns).ToList();
                    var indent = (bandColumns - row.Count) / 2d;
                    for (var x = 0; x < row.Count; x++)
                    {
                        positions[row[x]] = new RegistrationPosition((indent + x) * ColumnWidth, rowTop);
                        placed.Add(row[x]);
                    }

                    rowTop += RowHeight;
                }

                rowTop += LayerGap;
            }

            return ordered.Count == 0 ? top : rowTop - LayerGap;
        }

        // The mean place of a concept's already-placed broader concepts, or MaxValue when it has none.
        double BroaderPosition(string id, Dictionary<string, int> sequence)
        {
            var places = broadersOf.TryGetValue(id, out var broaders)
                ? broaders.Where(sequence.ContainsKey).Select(broader => (double)sequence[broader]).ToList()
                : [];
            return places.Count == 0 ? double.MaxValue : places.Average();
        }
    }

    /// <summary>
    /// The widest a row may be, in columns: about the square root of twice the elements to place,
    /// which keeps a band near two columns (about four row heights) of width per row of height.
    /// </summary>
    private static int ColumnsFor(int count) => Math.Max(MinimumColumns, (int)Math.Ceiling(Math.Sqrt(2d * count)));

    /// <summary>
    /// The acyclic layering subset and the cycles found. Per remaining knot (a strongly
    /// connected set of hierarchy edges), the edge whose (broader IRI, narrower IRI) pair sorts
    /// lowest is excluded and detection reruns, until nothing knots.
    /// </summary>
    private static (IReadOnlyList<SkosEdge> Acyclic, IReadOnlyList<SkosCycle> Cycles) BreakCycles(IReadOnlyList<SkosEdge> hierarchy)
    {
        var included = hierarchy.ToList();
        var cycles = new List<SkosCycle>();
        while (true)
        {
            var knot = FindKnot(included);
            if (knot is null)
            {
                return (included, cycles);
            }

            var knotEdges = included
                .Where(edge => knot.Contains(edge.FromId) && knot.Contains(edge.ToId))
                .ToList();
            var excluded = knotEdges
                .OrderBy(edge => edge.FromId, StringComparer.Ordinal)
                .ThenBy(edge => edge.ToId, StringComparer.Ordinal)
                .First();
            included.Remove(excluded);
            cycles.Add(new SkosCycle(
                knot.Order(StringComparer.Ordinal).ToList(),
                excluded.Id,
                knotEdges.SelectMany(edge => edge.Triples).ToList()));
        }
    }

    /// <summary>One strongly connected set of two or more nodes (or a self-loop), or null when none is left.</summary>
    private static HashSet<string>? FindKnot(IReadOnlyList<SkosEdge> edges)
    {
        var self = edges.FirstOrDefault(edge => edge.FromId == edge.ToId);
        if (self is not null)
        {
            return [self.FromId];
        }

        // Tarjan, iteratively: indexes and low-links over the hierarchy digraph.
        var next = edges.GroupBy(edge => edge.FromId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.ToId).ToList(), StringComparer.Ordinal);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var counter = 0;

        foreach (var start in next.Keys.Order(StringComparer.Ordinal))
        {
            if (index.ContainsKey(start))
            {
                continue;
            }

            var work = new Stack<(string Node, int Child)>();
            work.Push((start, 0));
            while (work.Count > 0)
            {
                (string node, int child) = work.Pop();
                if (child == 0)
                {
                    index[node] = low[node] = counter++;
                    stack.Push(node);
                    onStack.Add(node);
                }

                var children = next.TryGetValue(node, out var found) ? found : [];
                if (child < children.Count)
                {
                    work.Push((node, child + 1));
                    var target = children[child];
                    if (!index.TryGetValue(target, out var targetIndex))
                    {
                        work.Push((target, 0));
                    }
                    else if (onStack.Contains(target))
                    {
                        low[node] = Math.Min(low[node], targetIndex);
                    }
                }
                else
                {
                    if (low[node] == index[node])
                    {
                        var component = new HashSet<string>(StringComparer.Ordinal);
                        string popped;
                        do
                        {
                            popped = stack.Pop();
                            onStack.Remove(popped);
                            component.Add(popped);
                        }
                        while (popped != node);

                        if (component.Count > 1)
                        {
                            return component;
                        }
                    }

                    if (work.Count > 0)
                    {
                        var parent = work.Peek().Node;
                        low[parent] = Math.Min(low[parent], low[node]);
                    }
                }
            }
        }

        return null;
    }

    private static void AddTo(Dictionary<string, List<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(value);
    }
}
