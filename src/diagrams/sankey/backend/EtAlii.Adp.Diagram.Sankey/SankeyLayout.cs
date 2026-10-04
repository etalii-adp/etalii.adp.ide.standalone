using System.Runtime.CompilerServices;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>A rectangle on the canvas: left, top, width, height.</summary>
public readonly record struct SankeyBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CentreX => X + (Width / 2);

    public double CentreY => Y + (Height / 2);
}

/// <summary>Where one flow is drawn: how thick, and where along each end's edge its centre sits.</summary>
/// <param name="Thickness">The band's height, in canvas units - its value on the diagram's scale.</param>
/// <param name="SourceAt">Where its centre leaves the source, as a fraction of the source's height from its top.</param>
/// <param name="TargetAt">Where its centre reaches the target, likewise.</param>
/// <param name="Backward">Whether it runs back to a column at or left of its source's.</param>
public readonly record struct SankeyBand(double Thickness, double SourceAt, double TargetAt, bool Backward);

/// <summary>
/// Where everything in one document is drawn: each node's column, value and bar, and each flow's
/// band - all computed, none authored.
/// </summary>
/// <remarks>
/// <para>
/// <b>A Sankey diagram's geometry is its data.</b> A bar is as tall as the larger of what flows in
/// and what flows out, a band as thick as its value, both on one scale; so nothing here is a
/// position somebody chose, and a document never states one.
/// </para>
/// <para>
/// <b>Columns</b> come from the flows: a node sits one column right of the furthest node that
/// flows into it, with every cycle broken at the flow that closes it, unless it states its own
/// <c>column</c>.
/// </para>
/// <para>
/// <b>The order within a column is the document's</b>, top to bottom in the order the nodes are
/// written, and it is the one thing a reader arranges: dragging a node up or down its column moves
/// its entry among the others. Everything else about the vertical placement is relaxation - each
/// node is drawn towards the bands it shares with its neighbours, so a band runs as level as the
/// order allows - which <b>never reorders</b>: a node pulled past its neighbour stops at the gap.
/// </para>
/// <para>
/// <b>One instance per model, cached.</b> A model is immutable and replaced on every change, so
/// keying the cache on the instance means a pan reuses the layout and an edit recomputes it.
/// </para>
/// </remarks>
public sealed class SankeyLayout
{
    /// <summary>How many relaxation passes run, each weaker than the last.</summary>
    private const int Iterations = 24;

    private static readonly ConditionalWeakTable<SankeyModel, SankeyLayout> Cache = new();

    private SankeyLayout(
        IReadOnlyList<SankeyNode> nodes,
        IReadOnlyList<SankeyFlow> flows,
        IReadOnlyDictionary<string, SankeyBox> boxes,
        IReadOnlyDictionary<string, int> columns,
        IReadOnlyDictionary<string, double> values,
        IReadOnlyDictionary<string, SankeyBand> bands,
        IReadOnlyList<IReadOnlyList<string>> columnOrder,
        double scale)
    {
        Nodes = nodes;
        Flows = flows;
        Boxes = boxes;
        Columns = columns;
        Values = values;
        Bands = bands;
        ColumnOrder = columnOrder;
        Scale = scale;
    }

    /// <summary>The nodes that can be drawn: an id, unique across the document.</summary>
    public IReadOnlyList<SankeyNode> Nodes { get; }

    /// <summary>The flows that can be drawn: a unique id, both ends drawable, and not to itself.</summary>
    public IReadOnlyList<SankeyFlow> Flows { get; }

    /// <summary>Each drawable node's bar.</summary>
    public IReadOnlyDictionary<string, SankeyBox> Boxes { get; }

    /// <summary>Each drawable node's zero-based column.</summary>
    public IReadOnlyDictionary<string, int> Columns { get; }

    /// <summary>Each drawable node's value: the larger of its inflow and its outflow.</summary>
    public IReadOnlyDictionary<string, double> Values { get; }

    /// <summary>Each drawable flow's band.</summary>
    public IReadOnlyDictionary<string, SankeyBand> Bands { get; }

    /// <summary>The node ids of each column, top to bottom - the document's order.</summary>
    public IReadOnlyList<IReadOnlyList<string>> ColumnOrder { get; }

    /// <summary>Canvas units per unit of value.</summary>
    public double Scale { get; }

    /// <summary>The left edge of a column.</summary>
    public static double LeftOf(int column) => column * SankeyGeometry.ColumnPitch;

    /// <summary>The column nearest to a canvas x, never left of the first.</summary>
    public static int ColumnAt(double x) => Math.Max(0, (int)Math.Round((x - (SankeyGeometry.NodeWidth / 2)) / SankeyGeometry.ColumnPitch));

    /// <summary>The layout of <paramref name="model"/>, computed once per model instance.</summary>
    public static SankeyLayout Of(SankeyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Cache.GetValue(model, Compute);
    }

    private static SankeyLayout Compute(SankeyModel model)
    {
        // One id space across nodes and flows: the first entry with an id is drawn and every later
        // one reusing it is not, because the shared diff refuses an id twice in one rendering.
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var nodes = model.Nodes.Where(node => node.Id.Length > 0 && taken.Add(node.Id)).ToList();
        var nodeIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var flows = model.Flows
            .Where(flow => nodeIds.Contains(flow.From) && nodeIds.Contains(flow.To) && flow.From != flow.To && taken.Add(flow.Id))
            .ToList();

        double ValueOf(SankeyFlow flow) => Math.Max(0, flow.Value ?? 0);

        var inflow = nodes.ToDictionary(node => node.Id, _ => 0d, StringComparer.Ordinal);
        var outflow = nodes.ToDictionary(node => node.Id, _ => 0d, StringComparer.Ordinal);
        foreach (var flow in flows)
        {
            outflow[flow.From] += ValueOf(flow);
            inflow[flow.To] += ValueOf(flow);
        }

        var values = nodes.ToDictionary(node => node.Id, node => Math.Max(inflow[node.Id], outflow[node.Id]), StringComparer.Ordinal);
        var columns = ColumnsOf(nodes, flows);
        var columnCount = nodes.Count == 0 ? 0 : columns.Values.Max() + 1;

        // The document's order, column by column.
        var order = Enumerable.Range(0, columnCount)
            .Select(column => nodes.Where(node => columns[node.Id] == column).Select(node => node.Id).ToList())
            .ToList();

        // One scale for every bar and band: the fullest column fills the base height.
        var fullest = order.Count == 0 ? 0 : order.Max(column => column.Sum(id => values[id]));
        var scale = fullest > 0 ? SankeyGeometry.BaseHeight * model.Settings.Thickness / fullest : 0;
        var heights = nodes.ToDictionary(node => node.Id, node => Math.Max(SankeyGeometry.MinimumNodeHeight, values[node.Id] * scale), StringComparer.Ordinal);
        var extent = order.Count == 0 ? 0 : order.Max(column => column.Sum(id => heights[id]) + (Math.Max(0, column.Count - 1) * SankeyGeometry.NodeGap));

        var forward = flows.Where(flow => columns[flow.To] > columns[flow.From]).ToList();
        var outgoing = nodes.ToDictionary(node => node.Id, node => flows.Where(flow => flow.From == node.Id).ToList(), StringComparer.Ordinal);
        var incoming = nodes.ToDictionary(node => node.Id, node => flows.Where(flow => flow.To == node.Id).ToList(), StringComparer.Ordinal);
        var documentOrder = flows.Select((flow, index) => (flow.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);

        // Every column starts stacked from the top and centred on the tallest.
        var top = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var column in order)
        {
            var height = column.Sum(id => heights[id]) + (Math.Max(0, column.Count - 1) * SankeyGeometry.NodeGap);
            var y = (extent - height) / 2;
            foreach (var id in column)
            {
                top[id] = y;
                y += heights[id] + SankeyGeometry.NodeGap;
            }
        }

        double Centre(string id) => top[id] + (heights[id] / 2);
        double Thickness(SankeyFlow flow) => ValueOf(flow) * scale;

        // A node's bands, stacked in the order of the nodes at their other ends, so they do not cross at the bar.
        List<SankeyFlow> Out(string id) => [.. outgoing[id].OrderBy(flow => Centre(flow.To)).ThenBy(flow => documentOrder[flow.Id])];
        List<SankeyFlow> In(string id) => [.. incoming[id].OrderBy(flow => Centre(flow.From)).ThenBy(flow => documentOrder[flow.Id])];

        double OffsetOut(SankeyFlow flow) => Out(flow.From).TakeWhile(other => other.Id != flow.Id).Sum(Thickness);
        double OffsetIn(SankeyFlow flow) => In(flow.To).TakeWhile(other => other.Id != flow.Id).Sum(Thickness);

        var forwardIn = nodes.ToDictionary(node => node.Id, node => forward.Where(flow => flow.To == node.Id).ToList(), StringComparer.Ordinal);
        var forwardOut = nodes.ToDictionary(node => node.Id, node => forward.Where(flow => flow.From == node.Id).ToList(), StringComparer.Ordinal);

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var alpha = Math.Pow(0.99, iteration);

            // Right to left: each node towards where its outgoing bands would run level.
            for (var c = columnCount - 2; c >= 0; c--)
            {
                foreach (var id in order[c])
                {
                    Relax(id, forwardOut[id], flow => top[flow.To] + OffsetIn(flow) - OffsetOut(flow));
                }

                Resolve(order[c]);
            }

            // Left to right: each node towards where its incoming bands would run level.
            for (var c = 1; c < columnCount; c++)
            {
                foreach (var id in order[c])
                {
                    Relax(id, forwardIn[id], flow => top[flow.From] + OffsetOut(flow) - OffsetIn(flow));
                }

                Resolve(order[c]);
            }

            void Relax(string id, List<SankeyFlow> along, Func<SankeyFlow, double> levelTop)
            {
                var weight = along.Sum(ValueOf);
                if (weight <= 0)
                {
                    return;
                }

                var wanted = along.Sum(flow => levelTop(flow) * ValueOf(flow)) / weight;
                top[id] += (wanted - top[id]) * alpha;
            }
        }

        // Within the extent, in order, never closer than the gap: pushed down from the top, then up
        // from the bottom, then down once more in case the column is as tall as the extent.
        void Resolve(List<string> column)
        {
            for (var i = 0; i < column.Count; i++)
            {
                var floor = i == 0 ? 0 : top[column[i - 1]] + heights[column[i - 1]] + SankeyGeometry.NodeGap;
                top[column[i]] = Math.Max(top[column[i]], floor);
            }

            for (var i = column.Count - 1; i >= 0; i--)
            {
                var ceiling = i == column.Count - 1 ? extent : top[column[i + 1]] - SankeyGeometry.NodeGap;
                top[column[i]] = Math.Min(top[column[i]], ceiling - heights[column[i]]);
            }

            for (var i = 0; i < column.Count; i++)
            {
                var floor = i == 0 ? 0 : top[column[i - 1]] + heights[column[i - 1]] + SankeyGeometry.NodeGap;
                top[column[i]] = Math.Max(top[column[i]], floor);
            }
        }

        foreach (var column in order)
        {
            Resolve(column);
        }

        // Whole units: a position that changes in the fifth decimal on every recompute is a delta
        // nobody can see and every connection pays for.
        var boxes = nodes.ToDictionary(
            node => node.Id,
            node => new SankeyBox(LeftOf(columns[node.Id]), Math.Round(top[node.Id], 2), SankeyGeometry.NodeWidth, Math.Round(heights[node.Id], 2)),
            StringComparer.Ordinal);

        var bands = flows.ToDictionary(
            flow => flow.Id,
            flow =>
            {
                var thickness = Thickness(flow);
                return new SankeyBand(
                    Math.Round(Math.Max(SankeyGeometry.MinimumThickness, thickness), 2),
                    Fraction(OffsetOut(flow) + (thickness / 2), heights[flow.From], values[flow.From]),
                    Fraction(OffsetIn(flow) + (thickness / 2), heights[flow.To], values[flow.To]),
                    columns[flow.To] <= columns[flow.From]);
            },
            StringComparer.Ordinal);

        return new SankeyLayout(
            nodes,
            flows,
            boxes,
            columns,
            values,
            bands,
            [.. order.Select(column => (IReadOnlyList<string>)column)],
            scale);
    }

    /// <summary>Where along a bar a band's centre sits; the middle of a bar that carries nothing.</summary>
    private static double Fraction(double offset, double height, double value) =>
        value <= 0 || height <= 0 ? 0.5 : Math.Round(Math.Clamp(offset / height, 0, 1), 6);

    /// <summary>
    /// Each node's column: the one it states, or one past the furthest node that flows into it over
    /// the flows that close no cycle, so a source starts the diagram and a chain reads left to right.
    /// </summary>
    private static Dictionary<string, int> ColumnsOf(IReadOnlyList<SankeyNode> nodes, IReadOnlyList<SankeyFlow> flows)
    {
        var outgoing = nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var flow in flows)
        {
            outgoing[flow.From].Add(flow.To);
        }

        var forward = AcyclicEdges(nodes, outgoing);
        var incoming = nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        foreach (var target in forward.Values.SelectMany(targets => targets))
        {
            incoming[target]++;
        }

        var stated = nodes.Where(node => node.Column is not null).ToDictionary(node => node.Id, node => node.Column!.Value - 1, StringComparer.Ordinal);
        var column = nodes.ToDictionary(node => node.Id, node => stated.GetValueOrDefault(node.Id, 0), StringComparer.Ordinal);
        var ready = new Queue<string>(nodes.Where(node => incoming[node.Id] == 0).Select(node => node.Id));
        while (ready.Count > 0)
        {
            var id = ready.Dequeue();
            foreach (var target in forward[id])
            {
                if (!stated.ContainsKey(target))
                {
                    column[target] = Math.Max(column[target], column[id] + 1);
                }

                if (--incoming[target] == 0)
                {
                    ready.Enqueue(target);
                }
            }
        }

        return column;
    }

    /// <summary>The edges that do not close a cycle, found by a depth-first walk in document order.</summary>
    private static Dictionary<string, List<string>> AcyclicEdges(IReadOnlyList<SankeyNode> nodes, Dictionary<string, List<string>> outgoing)
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
                (string id, int next) = stack.Pop();
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
}
