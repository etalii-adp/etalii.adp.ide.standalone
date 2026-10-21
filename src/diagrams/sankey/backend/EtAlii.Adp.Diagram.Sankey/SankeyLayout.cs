using System.Runtime.CompilerServices;
using EtAlii.Adp.Layout;

namespace EtAlii.Adp.Diagram.Sankey;

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
        IReadOnlyDictionary<string, Rect> boxes,
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
    public IReadOnlyDictionary<string, Rect> Boxes { get; }

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

        var stacking = new Stacking(nodes, flows, heights, extent, scale);
        stacking.Stack(order);
        stacking.Relax(nodes, flows, columns, order, columnCount);

        // Whole units: a position that changes in the fifth decimal on every recompute is a delta
        // nobody can see and every connection pays for.
        var boxes = nodes.ToDictionary(
            node => node.Id,
            node => new Rect(LeftOf(columns[node.Id]), Math.Round(stacking.Top[node.Id], 2), SankeyGeometry.NodeWidth, Math.Round(heights[node.Id], 2)),
            StringComparer.Ordinal);

        return new SankeyLayout(
            nodes,
            flows,
            boxes,
            columns,
            values,
            BandsOf(flows, stacking, heights, values, columns),
            [.. order.Select(column => (IReadOnlyList<string>)column)],
            scale);
    }

    /// <summary>Each flow's band, from where the relaxation left its two ends.</summary>
    private static Dictionary<string, SankeyBand> BandsOf(
        List<SankeyFlow> flows,
        Stacking stacking,
        Dictionary<string, double> heights,
        Dictionary<string, double> values,
        Dictionary<string, int> columns)
    {
        return flows.ToDictionary(
            flow => flow.Id,
            flow =>
            {
                var thickness = stacking.Thickness(flow);
                return new SankeyBand(
                    Math.Round(Math.Max(SankeyGeometry.MinimumThickness, thickness), 2),
                    Fraction(stacking.OffsetOut(flow) + (thickness / 2), heights[flow.From], values[flow.From]),
                    Fraction(stacking.OffsetIn(flow) + (thickness / 2), heights[flow.To], values[flow.To]),
                    columns[flow.To] <= columns[flow.From]);
            },
            StringComparer.Ordinal);
    }

    private static double ValueOf(SankeyFlow flow) => Math.Max(0, flow.Value ?? 0);

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

        var ids = nodes.Select(node => node.Id).ToList();
        var forward = Layering.AcyclicEdges(ids, outgoing);
        var stated = nodes.Where(node => node.Column is not null).ToDictionary(node => node.Id, node => node.Column!.Value - 1, StringComparer.Ordinal);

        // A node that states its column keeps it, so the flows into it place nothing.
        return Layering.LongestPath(
            ids,
            forward.SelectMany(pair => pair.Value.Where(target => !stated.ContainsKey(target)).Select(target => (pair.Key, target))),
            CyclePolicy.Kahn,
            id => stated.GetValueOrDefault(id, 0));
    }

    /// <summary>
    /// The vertical placement of one layout's nodes: each node's top, and where each band meets
    /// its two bars.
    /// </summary>
    private sealed class Stacking
    {
        private readonly Dictionary<string, double> _heights;
        private readonly double _extent;
        private readonly double _scale;
        private readonly Dictionary<string, List<SankeyFlow>> _outgoing;
        private readonly Dictionary<string, List<SankeyFlow>> _incoming;
        private readonly Dictionary<string, int> _documentOrder;

        public Stacking(List<SankeyNode> nodes, List<SankeyFlow> flows, Dictionary<string, double> heights, double extent, double scale)
        {
            _heights = heights;
            _extent = extent;
            _scale = scale;
            _outgoing = nodes.ToDictionary(node => node.Id, node => flows.Where(flow => flow.From == node.Id).ToList(), StringComparer.Ordinal);
            _incoming = nodes.ToDictionary(node => node.Id, node => flows.Where(flow => flow.To == node.Id).ToList(), StringComparer.Ordinal);
            _documentOrder = flows.Select((flow, index) => (flow.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);
        }

        /// <summary>Each node's top edge.</summary>
        public Dictionary<string, double> Top { get; } = new(StringComparer.Ordinal);

        /// <summary>Every column starts stacked from the top and centred on the tallest.</summary>
        public void Stack(List<List<string>> order)
        {
            foreach (var column in order)
            {
                var height = column.Sum(id => _heights[id]) + (Math.Max(0, column.Count - 1) * SankeyGeometry.NodeGap);
                var y = (_extent - height) / 2;
                foreach (var id in column)
                {
                    Top[id] = y;
                    y += _heights[id] + SankeyGeometry.NodeGap;
                }
            }
        }

        public void Relax(List<SankeyNode> nodes, List<SankeyFlow> flows, Dictionary<string, int> columns, List<List<string>> order, int columnCount)
        {
            var forward = flows.Where(flow => columns[flow.To] > columns[flow.From]).ToList();
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
                        Pull(id, forwardOut[id], flow => Top[flow.To] + OffsetIn(flow) - OffsetOut(flow), alpha);
                    }

                    Resolve(order[c]);
                }

                // Left to right: each node towards where its incoming bands would run level.
                for (var c = 1; c < columnCount; c++)
                {
                    foreach (var id in order[c])
                    {
                        Pull(id, forwardIn[id], flow => Top[flow.From] + OffsetOut(flow) - OffsetIn(flow), alpha);
                    }

                    Resolve(order[c]);
                }
            }

            foreach (var column in order)
            {
                Resolve(column);
            }
        }

        public double Thickness(SankeyFlow flow) => ValueOf(flow) * _scale;

        public double OffsetOut(SankeyFlow flow) => Out(flow.From).TakeWhile(other => other.Id != flow.Id).Sum(Thickness);

        public double OffsetIn(SankeyFlow flow) => In(flow.To).TakeWhile(other => other.Id != flow.Id).Sum(Thickness);

        private void Pull(string id, List<SankeyFlow> along, Func<SankeyFlow, double> levelTop, double alpha)
        {
            var weight = along.Sum(ValueOf);
            if (weight <= 0)
            {
                return;
            }

            var wanted = along.Sum(flow => levelTop(flow) * ValueOf(flow)) / weight;
            Top[id] += (wanted - Top[id]) * alpha;
        }

        // Within the extent, in order, never closer than the gap: pushed down from the top, then up
        // from the bottom, then down once more in case the column is as tall as the extent.
        private void Resolve(List<string> column)
        {
            for (var i = 0; i < column.Count; i++)
            {
                var floor = i == 0 ? 0 : Top[column[i - 1]] + _heights[column[i - 1]] + SankeyGeometry.NodeGap;
                Top[column[i]] = Math.Max(Top[column[i]], floor);
            }

            for (var i = column.Count - 1; i >= 0; i--)
            {
                var ceiling = i == column.Count - 1 ? _extent : Top[column[i + 1]] - SankeyGeometry.NodeGap;
                Top[column[i]] = Math.Min(Top[column[i]], ceiling - _heights[column[i]]);
            }

            for (var i = 0; i < column.Count; i++)
            {
                var floor = i == 0 ? 0 : Top[column[i - 1]] + _heights[column[i - 1]] + SankeyGeometry.NodeGap;
                Top[column[i]] = Math.Max(Top[column[i]], floor);
            }
        }

        // A node's bands, stacked in the order of the nodes at their other ends, so they do not cross at the bar.
        private List<SankeyFlow> Out(string id) => [.. _outgoing[id].OrderBy(flow => Centre(flow.To)).ThenBy(flow => _documentOrder[flow.Id])];

        private List<SankeyFlow> In(string id) => [.. _incoming[id].OrderBy(flow => Centre(flow.From)).ThenBy(flow => _documentOrder[flow.Id])];

        private double Centre(string id) => Top[id] + (_heights[id] / 2);
    }
}
