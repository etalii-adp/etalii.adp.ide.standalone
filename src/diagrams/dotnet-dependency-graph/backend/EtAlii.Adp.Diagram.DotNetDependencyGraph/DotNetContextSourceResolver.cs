using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Makes a project or package node selectable: resolves an <c>element_id</c> against the
/// solution named by the enclosing selection level, verifies the node really is in that graph,
/// and fills in the detail a consumer shows.
/// </summary>
/// <remarks>
/// <para>
/// <b>The steps every resolver repeats are the base's</b> (code-structure FR-032). This says where
/// a graph is read from and what its elements are called: the solution the router names as the
/// body, whichever file the tab was opened at, through the shared watched-folder store; a project
/// by its path, a package by its id, and an edge by its reference kind.
/// </para>
/// <para>
/// <b>The target is the solution</b>, the routed body, as it has always been for this type: a
/// registration names its solution in its <c>body:</c> header, and the property provider reads
/// the graph from that path.
/// </para>
/// </remarks>
public sealed class DotNetContextSourceResolver(DiagramFileRouter router, DependencyGraphStore store, DependencyElementMapper mapper)
    : DiagramSnapshotSourceResolver<DependencyGraphModel>(router)
{
    /// <inheritdoc />
    protected override ToolOrigin Origin => Diagram.DependencyGraph.Origin;

    /// <inheritdoc />
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a .NET dependency graph.",
        "Unknown element.",
        "New element",
        "New relation");

    /// <inheritdoc />
    /// <remarks>Its store reads as it is asked, so the read is already done when this returns.</remarks>
    protected override ValueTask<DependencyGraphModel> LoadAsync(string bodyPath, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Load(bodyPath));

    /// <inheritdoc />
    protected override DependencyGraphModel Current(string bodyPath) => Load(bodyPath);

    private DependencyGraphModel Load(string bodyPath) => store.GetOrLoad(bodyPath);

    /// <inheritdoc />
    /// <remarks>
    /// A package is not a file in this workspace, so its path is its own id rather than a
    /// location - the honest answer, and one no consumer will try to reveal on disk.
    /// </remarks>
    protected override (IReadOnlyList<string> Path, string Text)? Describe(DependencyGraphModel snapshot, string elementId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Projects.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal)) is { } project)
        {
            return (Segments(project.RelativePath), project.Name);
        }

        if (snapshot.Packages.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal)) is { } package)
        {
            return ([package.PackageId], package.PackageId);
        }

        return snapshot.Edges.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal)) is { } edge
            ? ([edge.Id], LabelOf(edge))
            : null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Without the registration's stored positions: the target is the solution, and a position is
    /// no part of what a selection carries - the payload is the same wherever the element sits.
    /// </remarks>
    protected override DiagramElement? Drawn(DependencyGraphModel snapshot, string elementId) =>
        mapper.Elements(snapshot).FirstOrDefault(element => string.Equals(element.Id, elementId, StringComparison.Ordinal));

    /// <inheritdoc />
    /// <remarks>After every change to the solution or a project it names: a removed project clears the selection.</remarks>
    protected override IDisposable Follow(ShortGuid watchId, string bodyPath, string elementId, Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        return store.Watch(bodyPath, _ => changed());
    }

    /// <summary>An edge's own label: the reference kind, as the project file declares it.</summary>
    private static string LabelOf(DependsOnEdge edge) =>
        edge.Kind == DependsOnKind.Project ? "ProjectReference" : "PackageReference";

    private static string[] Segments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
}
