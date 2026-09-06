using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The computed layout: horizontal bands by primary <c>rdf:type</c>, grid-placed within a band
/// in projection order, the untyped band last - so a dataset's populations read at a glance and
/// the same file always opens the same way (Requirement 3.3, 4.4). A pure function, no physics,
/// no randomness; the layout overlay merges authored positions on top, element by element.
/// </summary>
internal static class RdfLayout
{
    /// <summary>
    /// The cell the layout reserves for one node, and therefore the box a viewport intersects
    /// it by. The client draws a narrower card inside it; the reserved cell is the honest box
    /// here, because a node whose cell is on screen is a node the reader can see.
    /// </summary>
    public const double CellWidth = 280;

    /// <inheritdoc cref="CellWidth" />
    public const double CellHeight = 160;

    private const double ColumnWidth = CellWidth;
    private const double RowHeight = CellHeight;
    private const double BandGap = 80;
    private const int Columns = 4;

    /// <summary>A position for every drawn node. Ids as the mapper speaks them.</summary>
    public static IReadOnlyDictionary<string, RegistrationPosition> Positions(RdfProjectionResult projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        // Bands keyed by the node's first type badge, ordered by that name - the untyped band,
        // keyed by the empty string, deliberately last rather than alphabetically first.
        var bands = projection.Nodes
            .GroupBy(node => node.Types.Count > 0 ? node.Types[0] : "")
            .OrderBy(band => band.Key.Length == 0 ? 1 : 0)
            .ThenBy(band => band.Key, StringComparer.Ordinal);

        var positions = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);
        var y = 0d;
        foreach (var band in bands)
        {
            var row = 0;
            var column = 0;
            foreach (var node in band)
            {
                positions[node.Id] = new RegistrationPosition(column * ColumnWidth, y + row * RowHeight);
                column++;
                if (column == Columns)
                {
                    column = 0;
                    row++;
                }
            }

            y += (row + (column > 0 ? 1 : 0)) * RowHeight + BandGap;
        }

        return positions;
    }
}
