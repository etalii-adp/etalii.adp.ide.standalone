namespace EtAlii.Adp.Layout;

/// <summary>Where a grid filled row by row put each item, and how much room the grid takes.</summary>
/// <param name="Cells">Each item's box, in the order given, measured from the grid's top-left corner.</param>
/// <param name="Width">The widest row, from its first item's left edge to its last item's right edge.</param>
/// <param name="Height">The rows' heights and the gaps between them; nothing below the last row.</param>
public sealed record GridPacking(IReadOnlyList<Rect> Cells, double Width, double Height);

/// <summary>
/// The grid a layout fills row by row, a fixed number of items to a row, for the elements it has
/// no better place for (code-structure FR-045).
/// </summary>
public static class GridPacker
{
    /// <summary>
    /// The top-left corner of the item at <paramref name="index"/> in a grid whose cells are all
    /// one size: the whole row and column of the index, never a fraction, so a grid is never a diagonal.
    /// </summary>
    /// <param name="index">The item's zero-based place in the order the grid is filled.</param>
    /// <param name="columns">How many items a row holds.</param>
    /// <param name="pitchX">The distance from one column's left edge to the next.</param>
    /// <param name="pitchY">The distance from one row's top edge to the next.</param>
    public static (double X, double Y) Cell(int index, int columns, double pitchX, double pitchY)
    {
        (int row, int column) = Math.DivRem(index, columns);
        return (column * pitchX, row * pitchY);
    }

    /// <summary>How many rows <paramref name="count"/> items fill, a part-filled last row included.</summary>
    public static int Rows(int count, int columns) => (count + columns - 1) / columns;

    /// <summary>
    /// A grid of items of their own sizes: each item to the right of the one before it on its row,
    /// and each row as tall as its tallest item.
    /// </summary>
    /// <param name="sizes">Each item's size, in the order the grid is filled.</param>
    /// <param name="columns">How many items a row holds.</param>
    /// <param name="gapX">The clear space between two items on a row.</param>
    /// <param name="gapY">The clear space between two rows.</param>
    public static GridPacking Pack(IReadOnlyList<(double Width, double Height)> sizes, int columns, double gapX, double gapY)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        var cells = new List<Rect>(sizes.Count);
        double x = 0;
        double y = 0;
        double width = 0;
        double rowHeight = 0;
        for (var index = 0; index < sizes.Count; index++)
        {
            if (index > 0 && index % columns == 0)
            {
                y += rowHeight + gapY;
                x = 0;
                rowHeight = 0;
            }

            (double itemWidth, double itemHeight) = sizes[index];
            cells.Add(new Rect(x, y, itemWidth, itemHeight));
            x += itemWidth + gapX;
            width = Math.Max(width, x - gapX);
            rowHeight = Math.Max(rowHeight, itemHeight);
        }

        return new GridPacking(cells, width, sizes.Count == 0 ? 0 : y + rowHeight);
    }
}
