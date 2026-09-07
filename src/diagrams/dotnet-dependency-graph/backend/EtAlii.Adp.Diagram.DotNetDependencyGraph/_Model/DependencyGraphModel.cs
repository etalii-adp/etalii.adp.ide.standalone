namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// The derived graph: what the canvas draws and what the property grid describes. Failures
/// travel with it, because "the diagram opens showing what it could resolve" needs the graph
/// and the reasons in one answer (Requirement 2.4).
/// </summary>
public sealed record DependencyGraphModel(
    IReadOnlyList<ProjectNode> Projects,
    IReadOnlyList<PackageNode> Packages,
    IReadOnlyList<DependsOnEdge> Edges,
    IReadOnlyList<SolutionFailure> Failures)
{
    /// <summary>An empty graph - what an unreadable solution yields, with its reason attached.</summary>
    public static DependencyGraphModel Empty { get; } = new([], [], [], []);
}
