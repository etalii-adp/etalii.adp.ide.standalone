using EtAlii.Adp.Layout;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// The computed layout of the shapes reading: cards grid-placed in discovery order, row heights
/// driven by the tallest card of each grid row - a pure function, no physics, no randomness. The
/// layout overlay merges authored positions on top for IRI-named cards; anonymous cards always
/// take exactly these positions (shacl-diagram Requirement 3.2).
/// </summary>
internal static class ShaclLayout
{
    private const double ColumnWidth = 320;
    private const double HeaderHeight = 64;
    private const double LineHeight = 22;
    private const double RowGap = 48;
    private const int Columns = 3;

    /// <summary>A position for every drawn card. Ids as the projection speaks them.</summary>
    public static IReadOnlyDictionary<string, RegistrationPosition> Positions(ShaclProjectionResult projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var cards = projection.Cards;
        var grid = GridPacker.Pack(
            [.. cards.Select(card => (ColumnWidth, HeaderHeight + (card.Targets.Count + card.Rows.Count) * LineHeight))],
            Columns,
            0,
            RowGap);

        var positions = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);
        for (var index = 0; index < cards.Count; index++)
        {
            positions[cards[index].Id] = new RegistrationPosition(grid.Cells[index].X, grid.Cells[index].Y);
        }

        return positions;
    }
}
