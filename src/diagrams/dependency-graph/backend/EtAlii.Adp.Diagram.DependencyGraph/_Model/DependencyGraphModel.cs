namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// A parsed <c>.dgr</c> document: what it declares, and where each declaration sits.
/// </summary>
/// <remarks>
/// A pure value. Every rule, every mapping and every layout decision is a function over this, so
/// each of them is testable from a string with no file, canvas or connection in sight.
/// </remarks>
/// <param name="Elements">The nodes, in document order.</param>
/// <param name="Relations">The directed depends-on edges, in document order.</param>
public sealed record DependencyGraphModel(
    IReadOnlyList<DependencyGraphElement> Elements,
    IReadOnlyList<DependencyGraphRelation> Relations)
{
    /// <summary>An empty model, for a document that declares nothing.</summary>
    public static DependencyGraphModel Empty { get; } = new([], []);
}
