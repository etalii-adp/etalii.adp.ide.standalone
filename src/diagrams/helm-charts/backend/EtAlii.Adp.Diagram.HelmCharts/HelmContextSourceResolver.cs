using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Hierarchy;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// Makes a chart node selectable: resolves an <c>element_id</c> against the chart named by the
/// enclosing selection level, verifies the node really is in that chart, and fills in the
/// detail a consumer shows (Requirements 8, 9.3).
/// </summary>
/// <remarks>
/// Registering this is the whole of it - the context service itself is untouched, which is
/// tech.md's rule that a new selectable thing is one resolver and never a bespoke RPC.
/// </remarks>
public sealed class HelmContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly IHelmChartStore _store;

    public HelmContextSourceResolver(DiagramFileRouter router, IHelmChartStore store)
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
        ContextSelectionSource source,
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

        if (_router.Route(parent.Target.ResolvedFullPath) is not DiagramRouted { Definition.Origin: var origin } ||
            origin != Diagram.HelmCharts.Origin)
        {
            // Not ours. Another type's resolver answers for its own elements; saying so plainly
            // is how several resolvers share one ContextSource member without fighting.
            return Rejected("The selected file is not a Helm chart diagram.");
        }

        var folder = FolderOf(parent.Target.ResolvedFullPath);
        if (folder is null)
        {
            return Rejected("The chart folder could not be resolved.");
        }

        var chart = _store.GetOrLoad(folder);
        var graph = HelmGraph.Derive(chart);

        var elementId = id.ElementId.Value;
        if (Locate(chart, graph, elementId) is not { } located)
        {
            return Rejected("Unknown element.");
        }

        return Resolve(watchId, rootPath, source, id, clientPath, folder, elementId, located.Path, located.Text);
    }

    /// <summary>Nothing nests inside a chart node; a subchart's own files are its own diagram's business.</summary>
    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    /// <summary>
    /// Re-resolves the element after every change to its chart: a deleted values file clears
    /// the selection, a change to what an edge names re-announces it (Requirement 8.3).
    /// </summary>
    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(onChange);

        // The target's path is already the chart root - Resolve put it there, because for this
        // type the folder IS what an element resolved within. Taking GetDirectoryName of it
        // here would silently watch the folder above and never hear a thing; that is the exact
        // bug the sibling module's ADeletedRole_ClearsTheSelection caught, pinned here too.
        var folder = IoPath.GetFullPath(level.Target.ResolvedFullPath);
        var elementId = level.Target.ElementId;
        var lastPath = level.RelativePath;
        var disposed = false;

        void OnChanged(object? sender, HelmChartChangedEventArgs args)
        {
            if (disposed ||
                !string.Equals(IoPath.GetFullPath(args.FolderPath), folder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var graph = HelmGraph.Derive(args.Chart);
            if (Locate(args.Chart, graph, elementId) is not { } located)
            {
                onChange(null); // Gone from the folder; the selection goes with it.
                return;
            }

            if (!located.Path.SequenceEqual(lastPath, StringComparer.Ordinal))
            {
                lastPath = located.Path;
                onChange(located.Path);
            }
        }

        _store.Changed += OnChanged;
        return new HelmNodeSubscription(() =>
        {
            disposed = true;
            _store.Changed -= OnChanged;
        });
    }

    /// <summary>
    /// The element's place and its words: a node answers with its own artifact's path (a
    /// dependency, having no file of its own, answers as the file that declares it), and an
    /// edge answers as its declaring side - the file a reader asking "why is this here" opens.
    /// </summary>
    private static (string[] Path, string Text)? Locate(HelmChart chart, HelmGraph graph, string elementId)
    {
        if (graph.Node(elementId) is { } node)
        {
            var path = node.RelativePath.Length > 0 ? node.RelativePath : DeclaringFile(chart);
            return (Segments(path), node.Name);
        }

        var edge = graph.Edges.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
        if (edge is null)
        {
            return null;
        }

        var sourcePath = graph.Node(edge.SourceId)?.RelativePath;
        var declaredIn = sourcePath is { Length: > 0 } ? sourcePath : DeclaringFile(chart);
        var label = edge.Label.Length > 0 ? $"{Verb(edge.Kind)} {edge.Label}" : Verb(edge.Kind);
        return (Segments(declaredIn), label);
    }

    /// <summary>Where dependencies are declared: <c>Chart.yaml</c>, or <c>requirements.yaml</c> for a legacy chart.</summary>
    private static string DeclaringFile(HelmChart chart) => chart.Legacy ? "requirements.yaml" : "Chart.yaml";

    private static string Verb(HelmEdgeKind kind) => kind switch
    {
        HelmEdgeKind.Declares => "declares",
        HelmEdgeKind.Resolves => "resolves to",
        HelmEdgeKind.Overrides => "overrides",
        HelmEdgeKind.Configures => "configures",
        HelmEdgeKind.Includes => "includes",
        _ => "",
    };

    /// <summary>
    /// The folder a registration marks. The <c>.adp</c> routes as its own body for a type with
    /// no extension, so the parent level's path is the registration itself.
    /// </summary>
    private static string? FolderOf(string registrationPath)
    {
        var folder = IoPath.GetDirectoryName(IoPath.GetFullPath(registrationPath));
        return folder is null ? null : IoPath.GetFullPath(folder);
    }

    private ValueTask<ContextLevelResolution> Resolve(
        ShortGuid watchId,
        string rootPath,
        ContextSelectionSource source,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        string folder,
        string elementId,
        string[] relativePath,
        string text)
    {
        // The client's version of the path is checked, never trusted.
        if (clientPath.Count > 0 && !clientPath.SequenceEqual(relativePath, StringComparer.Ordinal))
        {
            return Rejected("The path does not match the element.");
        }

        var level = new ContextResolvedLevel(
            source,
            id,
            relativePath,
            ContextScope.DiagramElement,
            new ContextTarget(
                ContextScope.DiagramElement,
                folder,
                IsContainer: false,
                SourceId: default,
                rootPath,
                watchId,
                elementId),
            new ContextLevelDetail
            {
                Element = new ElementDetail
                {
                    Text = text,
                    // Nothing on this diagram folds, so nothing has children for selection
                    // purposes and nothing is ever reported folded.
                    HasChildren = false,
                },
            },
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    private static string[] Segments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));
}
