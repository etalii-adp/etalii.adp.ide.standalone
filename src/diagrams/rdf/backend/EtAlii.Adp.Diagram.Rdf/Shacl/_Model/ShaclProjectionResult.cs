namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// What the shapes reading draws: cards up to the drawn-element budget - rows and chips travel
/// free with their card - plus the honest count (shacl-diagram Requirement 8.1).
/// </summary>
/// <param name="Cards">The drawn shapes, in discovery order.</param>
/// <param name="Edges">The drawn shape-to-shape edges - only those whose both endpoints made the cut.</param>
/// <param name="Shown">How many cards are drawn.</param>
/// <param name="Total">How many the document states.</param>
public sealed record ShaclProjectionResult(
    IReadOnlyList<ShaclCard> Cards,
    IReadOnlyList<ShaclEdge> Edges,
    int Shown,
    int Total)
{
    /// <summary>Whether the budget cut the diagram down - the state that shows the banner and withholds edits.</summary>
    public bool Truncated => Shown < Total;
}
