using EtAlii.Adp.Hierarchy;

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

        var positions = new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);
        var y = 0d;
        var column = 0;
        var tallest = 0d;
        foreach (var card in projection.Cards)
        {
            positions[card.Id] = new RegistrationPosition(column * ColumnWidth, y);
            tallest = Math.Max(tallest, HeaderHeight + (card.Targets.Count + card.Rows.Count) * LineHeight);
            column++;
            if (column == Columns)
            {
                column = 0;
                y += tallest + RowGap;
                tallest = 0;
            }
        }

        return positions;
    }
}
