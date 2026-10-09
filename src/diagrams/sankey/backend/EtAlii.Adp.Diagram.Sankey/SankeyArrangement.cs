namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// The order "Arrange diagram" gives the nodes of each column: the one whose bands cross least,
/// weighed by how thick the crossing bands are.
/// </summary>
/// <remarks>
/// <para>
/// <b>The order within a column is all a reader arranges</b> (<see cref="SankeyLayout"/>): columns
/// come from the flows and heights from the values, so the only clutter left to remove is bands
/// crossing each other. That is the layered crossing-minimisation problem, and the barycentre
/// method is its standard answer: sweep the columns left to right and back, each node taking the
/// place its neighbours' positions pull it to, weighted by how much flows between them.
/// </para>
/// <para>
/// <b>Positions are where a bar's middle sits in its column's stack of values</b>, as a fraction of
/// the column, which is how a Sankey diagram draws them; so a thick band pulls harder than a trickle,
/// and a thick crossing costs more than a thin one - two bands crossing cost the product of their values.
/// </para>
/// <para>
/// <b>Only an improvement is kept.</b> Every sweep is scored, and the arrangement returned is the
/// best one seen, starting with the reader's own; so arranging an arranged diagram changes nothing,
/// and a diagram that already has no crossings keeps its order.
/// </para>
/// </remarks>
public static class SankeyArrangement
{
    /// <summary>How many sweeps run, alternating direction.</summary>
    private const int Sweeps = 24;

    /// <summary>
    /// The node ids of each column, top to bottom, as the arrangement orders them - one list per
    /// column of <see cref="SankeyLayout.ColumnOrder"/>.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<string>> ColumnsOf(SankeyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var layout = SankeyLayout.Of(model);
        var columns = layout.Columns;
        var values = layout.Values;
        var flows = FlowsOf(layout);

        var current = layout.ColumnOrder.Select(column => column.ToList()).ToList();
        var best = current.Select(column => column.ToList()).ToList();
        var bestCost = Cost(best, flows, columns, values);

        for (var sweep = 0; sweep < Sweeps && bestCost > 0; sweep++)
        {
            var rightwards = sweep % 2 == 0;
            var positions = PositionsOf(current, values);
            var range = rightwards
                ? Enumerable.Range(1, Math.Max(0, current.Count - 1))
                : Enumerable.Range(0, Math.Max(0, current.Count - 1)).Reverse();
            foreach (var c in range)
            {
                // Each node towards the value-weighted mean of its neighbours on the side the sweep
                // comes from; a node with none there keeps where it is.
                var pulls = current[c].ToDictionary(
                    id => id,
                    id =>
                    {
                        var along = flows
                            .Where(flow => rightwards ? flow.Right == id : flow.Left == id)
                            .Select(flow => (Other: rightwards ? flow.Left : flow.Right, flow.Value))
                            .ToList();
                        var weight = along.Sum(entry => entry.Value);
                        return weight > 0 ? along.Sum(entry => positions[entry.Other] * entry.Value) / weight : positions[id];
                    },
                    StringComparer.Ordinal);

                // A stable sort: two nodes pulled to the same place keep their order.
                current[c] = [.. current[c].Select((id, index) => (id, index)).OrderBy(entry => pulls[entry.id]).ThenBy(entry => entry.index).Select(entry => entry.id)];
                positions = PositionsOf(current, values);
            }

            var cost = Cost(current, flows, columns, values);
            if (cost < bestCost - 1e-9)
            {
                bestCost = cost;
                best = current.Select(column => column.ToList()).ToList();
            }
        }

        return [.. best.Select(column => (IReadOnlyList<string>)column)];
    }

    /// <summary>
    /// How much the bands of the diagram as it reads now cross: for every two crossing bands, the
    /// product of their values. Zero means no band crosses another.
    /// </summary>
    public static double CrossingsOf(SankeyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var layout = SankeyLayout.Of(model);
        return Cost([.. layout.ColumnOrder.Select(column => column.ToList())], FlowsOf(layout), layout.Columns, layout.Values);
    }

    /// <summary>
    /// The drawable nodes in the order the document should list them: each column's nodes take the
    /// places that column's nodes held, in their new order, so a document that lists the nodes
    /// column by column, or interleaved, keeps its shape.
    /// </summary>
    public static IReadOnlyList<string> DocumentOrderOf(SankeyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var layout = SankeyLayout.Of(model);
        var queues = ColumnsOf(model).Select(column => new Queue<string>(column)).ToList();
        return [.. layout.Nodes.Select(node => queues[layout.Columns[node.Id]].Dequeue())];
    }

    /// <summary>The bands that run between two columns, each from its left end to its right end.</summary>
    private static List<(string Left, string Right, double Value)> FlowsOf(SankeyLayout layout)
    {
        var columns = layout.Columns;
        return layout.Flows
            .Where(flow => columns[flow.From] != columns[flow.To])
            .Select(flow => columns[flow.From] < columns[flow.To]
                ? (Left: flow.From, Right: flow.To, Value: Math.Max(0, flow.Value ?? 0))
                : (Left: flow.To, Right: flow.From, Value: Math.Max(0, flow.Value ?? 0)))
            .ToList();
    }

    /// <summary>Where each node's middle sits in its column, as a fraction of the column's total value.</summary>
    private static Dictionary<string, double> PositionsOf(List<List<string>> columns, IReadOnlyDictionary<string, double> values)
    {
        var positions = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            var total = column.Sum(Weight);
            var above = 0d;
            foreach (var id in column)
            {
                positions[id] = (above + (Weight(id) / 2)) / total;
                above += Weight(id);
            }

            continue;

            // A node that carries nothing still takes a little room, so empty nodes do not all collapse onto one point.
            double Weight(string id) => Math.Max(values[id], 1e-6);
        }

        return positions;
    }

    /// <summary>
    /// How much the bands cross: for every two bands whose columns overlap, the product of their
    /// values when their order at one end of the overlap differs from their order at the other.
    /// </summary>
    private static double Cost(
        List<List<string>> columns,
        List<(string Left, string Right, double Value)> flows,
        IReadOnlyDictionary<string, int> columnOf,
        IReadOnlyDictionary<string, double> values)
    {
        var positions = PositionsOf(columns, values);

        var cost = 0d;
        for (var i = 0; i < flows.Count; i++)
        {
            for (var j = i + 1; j < flows.Count; j++)
            {
                var a = flows[i];
                var b = flows[j];
                if (a.Left == b.Left || a.Right == b.Right)
                {
                    // Bands sharing an end are stacked by the layout so they never cross there.
                    continue;
                }

                double start = Math.Max(columnOf[a.Left], columnOf[b.Left]);
                double end = Math.Min(columnOf[a.Right], columnOf[b.Right]);
                if (end <= start)
                {
                    continue;
                }

                var before = At(a, start) - At(b, start);
                var after = At(a, end) - At(b, end);
                if (before * after < 0)
                {
                    cost += Math.Max(a.Value, 1e-6) * Math.Max(b.Value, 1e-6);
                }
            }
        }

        return cost;

        double At((string Left, string Right, double Value) flow, double column)
        {
            var from = columnOf[flow.Left];
            var to = columnOf[flow.Right];
            var t = (column - from) / (to - from);
            return positions[flow.Left] + ((positions[flow.Right] - positions[flow.Left]) * t);
        }
    }
}
