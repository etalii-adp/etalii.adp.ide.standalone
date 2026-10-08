using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Makes a project or package node selectable: resolves an <c>element_id</c> against the
/// solution named by the enclosing selection level, verifies the node really is in that graph,
/// and fills in the detail a consumer shows.
/// </summary>
/// <remarks>
/// Registering this is the whole of it - the context service itself is untouched, which is
/// tech.md's rule that a new selectable thing is one resolver and never a bespoke RPC.
/// </remarks>
public sealed class DotNetContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly DependencyGraphStore _store;

    public DotNetContextSourceResolver(DiagramFileRouter router, DependencyGraphStore store)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(store);
        _router = router;
        _store = store;
    }

    public bool CanResolve(ContextSource source) => source.SourceCase == ContextSource.SourceOneofCase.ElementId;

    public ValueTask<ContextLevelResolution> ResolveAsync(
        ShortGuid watchId,
        string rootPath,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        // A node is only ever selected inside its diagram: with no file level above it there is
        // nothing to verify it against, and an unverifiable selection is never recorded.
        if (parent is null || parent.Scope != ContextScope.Hierarchy)
        {
            return Rejected("A diagram element must be selected within its diagram.");
        }

        if (_router.Route(parent.Target.ResolvedFullPath, rootPath) is not DiagramRouted { Definition.Origin: var origin } routed ||
            origin != Diagram.DependencyGraph.Origin)
        {
            // Not ours. Another type's resolver answers for its own elements; saying so plainly
            // is how several resolvers share one ContextSource member without fighting.
            return Rejected("The selected file is not a .NET dependency graph.");
        }

        // The SOLUTION is the routed body, whichever file the tab was opened at: a canvas selection
        // nests under that file, which is the .adp registration as often as the .slnx itself. Taking
        // the parent's path as the solution loaded the registration as one and resolved nothing
        // (centralized-selection task 26); the router answers for both, as it does for the pipeline,
        // C4 and ansible resolvers.
        var solutionPath = IoPath.GetFullPath(routed.BodyPath ?? parent.Target.ResolvedFullPath);
        var graph = _store.GetOrLoad(solutionPath);
        var elementId = id.ElementId.Value;

        var project = graph.Projects.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
        if (project is not null)
        {
            return Resolve(
                watchId, rootPath, id, clientPath, solutionPath, elementId,
                Segments(project.RelativePath),
                new ContextLevelDetail { Element = new ElementDetail { Text = project.Name, HasChildren = false } });
        }

        var package = graph.Packages.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
        if (package is not null)
        {
            // A package is not a file in this workspace, so its path is its own id rather than
            // a location - the honest answer, and one no consumer will try to reveal on disk.
            return Resolve(
                watchId, rootPath, id, clientPath, solutionPath, elementId,
                [package.PackageId],
                new ContextLevelDetail { Element = new ElementDetail { Text = package.PackageId, HasChildren = false } });
        }

        var edge = graph.Edges.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
        if (edge is not null)
        {
            return Resolve(
                watchId, rootPath, id, clientPath, solutionPath, elementId,
                [edge.Id],
                new ContextLevelDetail { Element = new ElementDetail { Text = LabelOf(edge), HasChildren = false } });
        }

        return Rejected("Unknown element.");
    }

    /// <summary>Nothing nests inside a node of this graph; every project and package is its own selection.</summary>
    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    /// <summary>
    /// Nothing to track. The graph is recomputed on refresh and the session pushes the deltas;
    /// a selection whose element has gone is cleared by the same push. Returning an inert
    /// subscription says that plainly rather than registering a watcher that would fire on
    /// nothing.
    /// </summary>
    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange) =>
        new NoSubscription();

    private ValueTask<ContextLevelResolution> Resolve(
        ShortGuid watchId,
        string rootPath,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        string solutionPath,
        string elementId,
        string[] relativePath,
        ContextLevelDetail detail)
    {
        // The client's version of the path is checked, never trusted.
        if (clientPath.Count > 0 && !clientPath.SequenceEqual(relativePath, StringComparer.Ordinal))
        {
            return Rejected("The path does not match the element.");
        }

        var level = new ContextResolvedLevel(
            id,
            relativePath,
            ContextScope.DiagramElement,
            new ContextTarget(
                ContextScope.DiagramElement,
                solutionPath,
                IsContainer: false,
                SourceId: default,
                rootPath,
                watchId,
                elementId),
            detail,
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    /// <summary>An edge's own label: the reference kind, as the project file declares it.</summary>
    private static string LabelOf(DependsOnEdge edge) =>
        edge.Kind == DependsOnKind.Project ? "ProjectReference" : "PackageReference";

    private static string[] Segments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));

    /// <summary>An inert subscription, for a resolver with nothing of its own to watch.</summary>
    private sealed class NoSubscription : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
