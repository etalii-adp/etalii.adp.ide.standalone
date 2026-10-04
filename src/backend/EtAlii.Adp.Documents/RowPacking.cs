namespace EtAlii.Adp.Documents;

/// <summary>One thing a row canvas draws: where it starts and ends across, and how many rows it is tall.</summary>
/// <param name="Id">Its id, unique among the items packed together.</param>
/// <param name="Left">Its leftmost extent, label included, in the canvas's own x units.</param>
/// <param name="Right">Its rightmost extent, likewise.</param>
/// <param name="Rows">How many rows it occupies from its top row; one for everything but a tall note.</param>
public readonly record struct RowItem(string Id, double Left, double Right, int Rows = 1);

/// <summary>
/// The arrangement a row canvas offers as "Arrange diagram": every item on a row of its own
/// choosing, in as few rows as the items allow, with linked items on rows close together.
/// </summary>
/// <remarks>
/// <para>
/// <b>Across is the data; only the row is ours.</b> On a timeline or a hype cycle an item's
/// horizontal extent is its dates, so an arrangement may change nothing but rows. That makes the
/// problem interval partitioning, which a sweep from left to right solves exactly: an item goes on
/// a row that is free where it starts, and a new row is opened only when every row is busy there -
/// at which point that many items overlap one point, so no arrangement could use fewer.
/// </para>
/// <para>
/// <b>Which free row is the clutter question.</b> Among the rows that are free, an item takes the one
/// nearest the rows of the items it is linked to that are already placed, so a chain of
/// non-overlapping linked items runs along one row and its links are drawn level. Then whole rows
/// swap places while that shortens the links in total, which pulls linked rows together without
/// adding a row.
/// </para>
/// <para>
/// <b>Deterministic</b>: ties fall to the lower row and the original order, so arranging an arranged
/// diagram changes nothing.
/// </para>
/// </remarks>
public static class RowPacking
{
    /// <summary>The most whole passes the row-swapping makes before it settles for what it has.</summary>
    private const int SwapPasses = 64;

    /// <summary>
    /// The zero-based row of each item: no two items share a row where they overlap or come closer
    /// than <paramref name="gap"/> across.
    /// </summary>
    /// <param name="items">The items, in the document's order.</param>
    /// <param name="links">The links between them, by id; a link naming an unknown id is ignored.</param>
    /// <param name="gap">The least clear space between two items on one row.</param>
    public static IReadOnlyDictionary<string, int> Pack(
        IReadOnlyList<RowItem> items,
        IReadOnlyList<(string From, string To)> links,
        double gap)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(links);

        var order = items
            .Select((item, index) => (Item: item with { Rows = Math.Max(1, item.Rows) }, Index: index))
            .OrderBy(entry => entry.Item.Left)
            .ThenBy(entry => entry.Item.Right)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Item)
            .ToList();
        var known = order.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var neighbours = order.ToDictionary(item => item.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach ((string from, string to) in links)
        {
            if (from != to && known.Contains(from) && known.Contains(to))
            {
                neighbours[from].Add(to);
                neighbours[to].Add(from);
            }
        }

        // Where each row is next free, across.
        var freeFrom = new List<double>();
        var rows = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in order)
        {
            bool Fits(int top) => Enumerable.Range(top, item.Rows).All(row => row >= freeFrom.Count || freeFrom[row] <= item.Left);

            var placed = neighbours[item.Id].Where(rows.ContainsKey).Select(id => rows[id]).ToList();
            var existing = Enumerable.Range(0, Math.Max(0, freeFrom.Count - item.Rows + 1)).Where(Fits).ToList();
            int row;
            if (existing.Count == 0)
            {
                // Every row is busy here: the lowest top from which the item fits, opening rows below.
                row = Enumerable.Range(0, freeFrom.Count + 1).First(Fits);
            }
            else if (placed.Count == 0)
            {
                row = existing[0];
            }
            else
            {
                var wanted = placed.Average();
                row = existing.OrderBy(candidate => Math.Abs(candidate - wanted)).ThenBy(candidate => candidate).First();
            }

            while (freeFrom.Count < row + item.Rows)
            {
                freeFrom.Add(double.NegativeInfinity);
            }

            for (var r = row; r < row + item.Rows; r++)
            {
                freeFrom[r] = item.Right + gap;
            }

            rows[item.Id] = row;
        }

        return SwapRows(order, rows, neighbours, freeFrom.Count);
    }

    /// <summary>
    /// Swaps neighbouring rows while that makes the links shorter in total. A row touched by an item
    /// taller than one row stays where it is, because swapping it would tear that item in two.
    /// </summary>
    private static Dictionary<string, int> SwapRows(
        List<RowItem> items,
        Dictionary<string, int> rows,
        Dictionary<string, List<string>> neighbours,
        int rowCount)
    {
        var pinned = new HashSet<int>();
        foreach (var item in items.Where(item => item.Rows > 1))
        {
            for (var r = rows[item.Id]; r < rows[item.Id] + item.Rows; r++)
            {
                pinned.Add(r);
            }
        }

        // Which row each original row is drawn at now.
        var at = Enumerable.Range(0, rowCount).ToArray();
        var links = items
            .SelectMany(item => neighbours[item.Id].Where(other => string.CompareOrdinal(item.Id, other) < 0).Select(other => (From: item.Id, To: other)))
            .ToList();

        double Cost() => links.Sum(link => Math.Abs(at[rows[link.From]] - at[rows[link.To]]));

        var cost = Cost();
        for (var pass = 0; pass < SwapPasses; pass++)
        {
            var improved = false;
            for (var position = 0; position + 1 < rowCount; position++)
            {
                var upper = Array.IndexOf(at, position);
                var lower = Array.IndexOf(at, position + 1);
                if (pinned.Contains(upper) || pinned.Contains(lower))
                {
                    continue;
                }

                (at[upper], at[lower]) = (at[lower], at[upper]);
                var swapped = Cost();
                if (swapped < cost - 1e-9)
                {
                    cost = swapped;
                    improved = true;
                }
                else
                {
                    (at[upper], at[lower]) = (at[lower], at[upper]);
                }
            }

            if (!improved)
            {
                break;
            }
        }

        return rows.ToDictionary(entry => entry.Key, entry => at[entry.Value], StringComparer.Ordinal);
    }
}
