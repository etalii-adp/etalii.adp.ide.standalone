using System.Runtime.CompilerServices;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>A rectangle on the canvas: left, top, width, height.</summary>
public readonly record struct SupplyChainBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CentreX => X + (Width / 2);

    public double CentreY => Y + (Height / 2);
}

/// <summary>
/// Where everything in one document is drawn: each node's box, each group's frame, and which
/// entries can be drawn at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Computed with authored overrides.</b> A node that states <c>x</c> and <c>y</c> is drawn there;
/// every other node is placed by <see cref="Arrange"/>, a layered left-to-right layout computed over
/// the WHOLE document, so a node that is dragged - and so gains coordinates - moves nobody else.
/// Arrange diagram writes the computed positions into the document for every node.
/// </para>
/// <para>
/// <b>A group's frame is never authored.</b> It is the box around its members, so it follows them;
/// an empty group draws nothing and the validator says so.
/// </para>
/// <para>
/// <b>One instance per model, cached.</b> A model is immutable and replaced on every change, so
/// keying the cache on the instance means a pan reuses the layout and an edit recomputes it.
/// </para>
/// </remarks>
public sealed class SupplyChainLayout
{
    /// <summary>The horizontal distance between the left edges of two neighbouring layers.</summary>
    public const double LayerPitch = SupplyChainGeometry.NodeWidth + 128;

    /// <summary>The vertical distance between the tops of two nodes stacked in one band.</summary>
    public const double RowPitch = SupplyChainGeometry.NodeHeight + 28;

    /// <summary>The clear space between two bands that share a layer.</summary>
    public const double BandGap = 32;

    private static readonly ConditionalWeakTable<SupplyChainModel, SupplyChainLayout> Cache = new();

    private SupplyChainLayout(
        IReadOnlyList<SupplyChainGroup> groups,
        IReadOnlyList<SupplyChainNode> nodes,
        IReadOnlyList<SupplyChainFlow> flows,
        IReadOnlyDictionary<string, SupplyChainBox> nodeBoxes,
        IReadOnlyDictionary<string, SupplyChainBox> groupBoxes,
        IReadOnlyDictionary<string, (double X, double Y)> arranged)
    {
        Groups = groups;
        Nodes = nodes;
        Flows = flows;
        NodeBoxes = nodeBoxes;
        GroupBoxes = groupBoxes;
        Arranged = arranged;
    }

    /// <summary>The groups that can be drawn: an id, unique across the document, and at least one member.</summary>
    public IReadOnlyList<SupplyChainGroup> Groups { get; }

    /// <summary>The nodes that can be drawn: an id unique across the document, and a known stage.</summary>
    public IReadOnlyList<SupplyChainNode> Nodes { get; }

    /// <summary>The flows that can be drawn: a unique id, and both ends drawable nodes.</summary>
    public IReadOnlyList<SupplyChainFlow> Flows { get; }

    /// <summary>Each drawable node's box, authored or arranged.</summary>
    public IReadOnlyDictionary<string, SupplyChainBox> NodeBoxes { get; }

    /// <summary>Each drawn group's frame.</summary>
    public IReadOnlyDictionary<string, SupplyChainBox> GroupBoxes { get; }

    /// <summary>Every drawable node's top-left as the layered layout places it, ignoring what the document states.</summary>
    public IReadOnlyDictionary<string, (double X, double Y)> Arranged { get; }

    /// <summary>The layout of <paramref name="model"/>, computed once per model instance.</summary>
    public static SupplyChainLayout Of(SupplyChainModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Cache.GetValue(model, Compute);
    }

    private static SupplyChainLayout Compute(SupplyChainModel model)
    {
        // One id space across groups, nodes and flows: the first entry with an id is drawn and every
        // later one reusing it is not, because the shared diff refuses an id twice in one rendering.
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var declaredGroups = model.Groups.Where(group => group.Id.Length > 0 && taken.Add(group.Id)).ToList();
        var nodes = model.Nodes.Where(node => node.Id.Length > 0 && SupplyChainNodeTypes.IsKnown(node.Type) && taken.Add(node.Id)).ToList();
        var nodeIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var flows = model.Flows
            .Where(flow => flow.Id.Length > 0 && nodeIds.Contains(flow.From) && nodeIds.Contains(flow.To) && flow.From != flow.To && taken.Add(flow.Id))
            .ToList();

        var groupIds = declaredGroups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        string? GroupOf(SupplyChainNode node) => groupIds.Contains(node.Group) ? node.Group : null;

        var arranged = Arrange(nodes, flows, GroupOf);

        var nodeBoxes = nodes.ToDictionary(
            node => node.Id,
            node =>
            {
                var (x, y) = node.IsPlaced ? (node.X!.Value, node.Y!.Value) : arranged[node.Id];
                return new SupplyChainBox(x, y, SupplyChainGeometry.NodeWidth, SupplyChainGeometry.NodeHeight);
            },
            StringComparer.Ordinal);

        var groupBoxes = new Dictionary<string, SupplyChainBox>(StringComparer.Ordinal);
        foreach (var group in declaredGroups)
        {
            var members = nodes.Where(node => GroupOf(node) == group.Id).Select(node => nodeBoxes[node.Id]).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            var left = members.Min(box => box.X) - SupplyChainGeometry.GroupPadding;
            var top = members.Min(box => box.Y) - SupplyChainGeometry.GroupHeader;
            var right = members.Max(box => box.Right) + SupplyChainGeometry.GroupPadding;
            var bottom = members.Max(box => box.Bottom) + SupplyChainGeometry.GroupPadding;
            groupBoxes[group.Id] = new SupplyChainBox(left, top, right - left, bottom - top);
        }

        return new SupplyChainLayout(
            [.. declaredGroups.Where(group => groupBoxes.ContainsKey(group.Id))],
            nodes,
            flows,
            nodeBoxes,
            groupBoxes,
            arranged);
    }

    /// <summary>
    /// The layered left-to-right layout: each node's top-left, with goods flowing to the right and
    /// every group kept together in a band of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Layers</b> are the longest path from a node with no supplier, over the flows with every
    /// cycle broken at the edge that closes it - so a recycling loop does not stretch the chain.
    /// </para>
    /// <para>
    /// <b>Bands</b> are the groups, plus one band for the ungrouped nodes. A band spans the layers
    /// its members occupy and is as tall as its fullest layer, which is what keeps every group's
    /// frame clear of every other group's nodes: frames are the boxes around members, and two bands
    /// never share a stretch of a layer.
    /// </para>
    /// <para>
    /// <b>Order</b> is by barycentre: bands, and nodes within a band, are sorted a few times by the
    /// mean position of what they are connected to, which is what untangles the flows. Bands are
    /// then dropped onto a per-layer skyline in that order, so a band covering the first two layers
    /// and one covering the last two can sit side by side rather than one above the other.
    /// </para>
    /// </remarks>
    internal static Dictionary<string, (double X, double Y)> Arrange(
        IReadOnlyList<SupplyChainNode> nodes,
        IReadOnlyList<SupplyChainFlow> flows,
        Func<SupplyChainNode, string?> groupOf)
    {
        var result = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        if (nodes.Count == 0)
        {
            return result;
        }

        var index = nodes.Select((node, position) => (node.Id, position)).ToDictionary(pair => pair.Id, pair => pair.position, StringComparer.Ordinal);
        var outgoing = nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var flow in flows)
        {
            outgoing[flow.From].Add(flow.To);
        }

        var forward = AcyclicEdges(nodes, outgoing);
        var layer = Layers(nodes, forward);

        var neighbours = nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var flow in flows)
        {
            neighbours[flow.From].Add(flow.To);
            neighbours[flow.To].Add(flow.From);
        }

        // The bands, in document order of their first member.
        const string ungrouped = "\u0000ungrouped";
        var bands = nodes
            .GroupBy(node => groupOf(node) ?? ungrouped, StringComparer.Ordinal)
            .Select(group => group.Select(node => node.Id).ToList())
            .ToList();
        var bandOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var band = 0; band < bands.Count; band++)
        {
            foreach (var id in bands[band])
            {
                bandOf[id] = band;
            }
        }

        // A position to sort by, refined each pass: start from document order.
        var rank = nodes.ToDictionary(node => node.Id, node => (double)index[node.Id], StringComparer.Ordinal);
        double Barycentre(string id) => neighbours[id].Count == 0 ? rank[id] : neighbours[id].Average(other => rank[other]);

        List<int> bandOrder = [.. Enumerable.Range(0, bands.Count)];
        // Within a band, the members of each layer in their order.
        var rows = new Dictionary<(int Band, int Layer), List<string>>();
        for (var pass = 0; pass < 8; pass++)
        {
            var bandScore = bandOrder.ToDictionary(band => band, band => bands[band].Average(Barycentre));
            bandOrder = [.. bandOrder.OrderBy(band => bandScore[band]).ThenBy(band => bands[band].Min(id => index[id]))];

            rows.Clear();
            foreach (var band in bandOrder)
            {
                foreach (var byLayer in bands[band].GroupBy(id => layer[id]))
                {
                    rows[(band, byLayer.Key)] = [.. byLayer.OrderBy(Barycentre).ThenBy(id => index[id])];
                }
            }

            // The global rank: bands in order, then row within the band.
            var offset = 0.0;
            foreach (var band in bandOrder)
            {
                var height = 0;
                foreach (var ((rowBand, _), members) in rows)
                {
                    if (rowBand != band)
                    {
                        continue;
                    }

                    for (var row = 0; row < members.Count; row++)
                    {
                        rank[members[row]] = offset + row;
                    }

                    height = Math.Max(height, members.Count);
                }

                offset += height + 1;
            }
        }

        // Drop the bands onto a per-layer skyline, in order.
        var layers = layer.Values.Max() + 1;
        var skyline = new double[layers];
        foreach (var band in bandOrder)
        {
            var spanned = bands[band].Select(id => layer[id]).ToList();
            var (first, last) = (spanned.Min(), spanned.Max());
            var grouped = groupOf(nodes[index[bands[band][0]]]) is not null;
            var header = grouped ? SupplyChainGeometry.GroupHeader : 0;
            var footer = grouped ? SupplyChainGeometry.GroupPadding : 0;

            var top = 0.0;
            for (var l = first; l <= last; l++)
            {
                top = Math.Max(top, skyline[l]);
            }

            var tallest = 0;
            for (var l = first; l <= last; l++)
            {
                if (!rows.TryGetValue((band, l), out var members))
                {
                    continue;
                }

                tallest = Math.Max(tallest, members.Count);
                for (var row = 0; row < members.Count; row++)
                {
                    result[members[row]] = (l * LayerPitch, top + header + (row * RowPitch));
                }
            }

            var bottom = top + header + (tallest * RowPitch) - (RowPitch - SupplyChainGeometry.NodeHeight) + footer + BandGap;
            for (var l = first; l <= last; l++)
            {
                skyline[l] = bottom;
            }
        }

        return result;
    }

    /// <summary>The flows that do not close a cycle, found by a depth-first walk in document order.</summary>
    private static Dictionary<string, List<string>> AcyclicEdges(IReadOnlyList<SupplyChainNode> nodes, Dictionary<string, List<string>> outgoing)
    {
        var forward = nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var start in nodes)
        {
            if (state.ContainsKey(start.Id))
            {
                continue;
            }

            // An explicit stack: a long chain must not overflow the call stack.
            var stack = new Stack<(string Id, int Next)>();
            stack.Push((start.Id, 0));
            state[start.Id] = 1;
            while (stack.Count > 0)
            {
                var (id, next) = stack.Pop();
                var targets = outgoing[id];
                if (next >= targets.Count)
                {
                    state[id] = 2;
                    continue;
                }

                stack.Push((id, next + 1));
                var target = targets[next];
                if (!state.TryGetValue(target, out var seen))
                {
                    forward[id].Add(target);
                    state[target] = 1;
                    stack.Push((target, 0));
                }
                else if (seen == 2)
                {
                    forward[id].Add(target);
                }

                // seen == 1 is a node still on the walk: this flow closes a cycle and is left out.
            }
        }

        return forward;
    }

    /// <summary>Each node's layer: the longest path to it from a node nothing supplies.</summary>
    private static Dictionary<string, int> Layers(IReadOnlyList<SupplyChainNode> nodes, Dictionary<string, List<string>> forward)
    {
        var incoming = nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        foreach (var targets in forward.Values)
        {
            foreach (var target in targets)
            {
                incoming[target]++;
            }
        }

        var layer = nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var ready = new Queue<string>(nodes.Where(node => incoming[node.Id] == 0).Select(node => node.Id));
        while (ready.Count > 0)
        {
            var id = ready.Dequeue();
            foreach (var target in forward[id])
            {
                layer[target] = Math.Max(layer[target], layer[id] + 1);
                if (--incoming[target] == 0)
                {
                    ready.Enqueue(target);
                }
            }
        }

        return layer;
    }
}
