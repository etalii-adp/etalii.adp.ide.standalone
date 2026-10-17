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

    /// <summary>
    /// The same text read through the module's binding, which the menus, the rows, the findings and the
    /// payloads come from; null for a model the parser read on its own. Until every edit is written
    /// through the binding a document carries both readings (dependency-graph-disl-fbl, task 7): this
    /// one, and the parser's, whose line ranges the writer splices at.
    /// </summary>
    public DependencyGraphBody? Body { get; init; }
}
