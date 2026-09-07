namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>What kind of thing an edge points at. The two reference kinds, drawn distinctly.</summary>
public enum DependsOnKind
{
    /// <summary>A <c>ProjectReference</c>: project to project (Requirement 3.2).</summary>
    Project,

    /// <summary>A <c>PackageReference</c>: project to package (Requirement 3.3).</summary>
    Package,
}

/// <summary>
/// One declared dependency, directed from the project that declares it to the thing it depends
/// on. Every edge corresponds to a declaration in a project file: none is invented, and one
/// that cannot be resolved is reported rather than dropped.
/// </summary>
public sealed record DependsOnEdge(string Id, string FromElementId, string ToElementId, DependsOnKind Kind);
