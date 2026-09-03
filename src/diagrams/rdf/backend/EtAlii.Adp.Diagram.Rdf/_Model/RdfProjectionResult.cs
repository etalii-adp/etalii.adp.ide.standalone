namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// What one reading draws: nodes and edges up to the drawn-element budget, plus the honest count
/// (Requirement 8).
/// </summary>
/// <param name="Nodes">The drawn resources, in document order of first appearance.</param>
/// <param name="Edges">The drawn edges - only those whose both endpoints made the cut.</param>
/// <param name="Shown">How many nodes are drawn.</param>
/// <param name="Total">How many the document states.</param>
public sealed record RdfProjectionResult(
    IReadOnlyList<RdfProjectionNode> Nodes,
    IReadOnlyList<RdfProjectionEdge> Edges,
    int Shown,
    int Total)
{
    /// <summary>Whether the budget cut the graph down - the state that shows the banner and withholds edits.</summary>
    public bool Truncated => Shown < Total;
}
