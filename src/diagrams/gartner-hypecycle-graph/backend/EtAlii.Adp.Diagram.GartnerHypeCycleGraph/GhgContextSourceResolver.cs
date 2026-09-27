using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Resolves a trend or influence of a <c>.ghg</c> document to a selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>It lands with the session factory</b>, as FDG's did: registering a session factory makes the
/// host's <c>DrawnConnections</c> guard open this module's example, and every influence it draws must
/// resolve to a selection.
/// </para>
/// <para>
/// <b>The element a selection describes is the one the canvas was sent.</b> Its payload comes from
/// <see cref="GhgElementMapper"/> rather than being assembled here, so the stream and the selection
/// can never describe one trend two ways.
/// </para>
/// <para>
/// <b>A placement or a finished connect gesture resolves too</b>: <c>new:x,y</c> names the trend a
/// drop is about to create and <c>rel:from-&gt;to</c> (each end optionally carrying its attachment,
/// see <see cref="GhgGestures"/>) the influence a gesture proposes. Each resolves to a target the
/// action provider reads back out, and lives for one execution: never selected, tracked or written.
/// </para>
/// </remarks>
public sealed class GhgContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly IGhgDocumentStore _documents;
    private readonly GhgElementMapper _mapper;

    public GhgContextSourceResolver(DiagramFileRouter router, IGhgDocumentStore documents, GhgElementMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        _router = router;
        _documents = documents;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public bool CanResolve(ContextSource source) => source.SourceCase == ContextSource.SourceOneofCase.ElementId;

    /// <inheritdoc />
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
        cancellationToken.ThrowIfCancellationRequested();

        // An element is only ever selected inside a diagram: with no file level above it there is
        // nothing to verify it against, and an unverifiable selection is never recorded.
        if (parent is null || parent.Scope != ContextScope.Hierarchy)
        {
            return Rejected("A diagram element must be selected within its diagram.");
        }

        if (_router.Route(parent.Target.ResolvedFullPath, rootPath) is not DiagramRouted routed ||
            routed.Definition.Origin != Diagram.HypeCycleGraph.Origin ||
            routed.BodyPath is not { Length: > 0 } bodyPath)
        {
            return Rejected("The selected file is not a hype cycle graph.");
        }

        var model = _documents.GetOrLoad(bodyPath).Model;
        var elementId = id.ElementId.Value;

        if (GestureIds.TryParsePlacement(elementId, out _, out _) || GestureIds.TryParseRelation(elementId, out _, out _))
        {
            var proposed = GestureIds.IsPlacement(elementId) ? "New trend" : "New influence";
            return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(new ContextResolvedLevel(
                source,
                id,
                [proposed],
                ContextScope.DiagramElement,
                new ContextTarget(
                    ContextScope.DiagramElement,
                    bodyPath,
                    IsContainer: false,
                    SourceId: default,
                    rootPath,
                    watchId,
                    elementId,
                    routed.Definition.Origin),
                new ContextLevelDetail { Element = new ElementDetail { Text = proposed } },
                this)));
        }

        var described = Describe(model, elementId);
        if (described is null)
        {
            return Rejected("That is no longer in this graph.");
        }

        var (path, text) = described.Value;

        // The client's path is checked, never trusted: it is what the client believes it selected,
        // and the id is what it actually selected.
        if (clientPath.Count > 0 && !clientPath.SequenceEqual(path, StringComparer.Ordinal))
        {
            return Rejected("The path does not match the element.");
        }

        var detail = new ContextLevelDetail
        {
            Element = new ElementDetail
            {
                Text = text,
                HasChildren = false,
                Folded = false,
                Linked = false,
            },
        };

        var drawn = _mapper.Visible(model, DiagramViewport.Unbounded).FirstOrDefault(candidate => candidate.Id == elementId);
        if (drawn is not null)
        {
            detail.Element.ElementType = drawn.Type;
            detail.Element.Payload = new Any
            {
                TypeUrl = drawn.PayloadTypeUrl,
                Value = ByteString.CopyFrom(drawn.Payload.Span),
            };
        }

        var level = new ContextResolvedLevel(
            source,
            id,
            path,
            ContextScope.DiagramElement,
            new ContextTarget(
                ContextScope.DiagramElement,
                bodyPath,
                IsContainer: false,
                SourceId: default,
                rootPath,
                watchId,
                elementId,
                routed.Definition.Origin),
            detail,
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    /// <inheritdoc />
    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    /// <inheritdoc />
    public IDisposable Track(
        ShortGuid watchId,
        string rootPath,
        ContextResolvedLevel level,
        Action<IReadOnlyList<string>?> onChange)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(onChange);
        _ = watchId;
        _ = rootPath;

        var bodyPath = level.Target.ResolvedFullPath;
        var elementId = level.Target.ElementId;

        void OnChanged(object? sender, GhgDocumentChangedEventArgs args)
        {
            if (!string.Equals(args.Path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Re-read rather than carrying the previous reading: a rename keeps the id while changing
            // everything shown, and a removal clears the selection.
            onChange(Describe(_documents.GetOrLoad(bodyPath).Model, elementId)?.Path);
        }

        _documents.Changed += OnChanged;
        return new Unsubscriber(() => _documents.Changed -= OnChanged);
    }

    /// <summary>What a selection of <paramref name="id"/> shows, or null when nothing drawn has that id.</summary>
    private static (IReadOnlyList<string> Path, string Text)? Describe(GhgModel model, string id)
    {
        // The first entry with the id, trend before influence, as the mapper draws it.
        if (model.Trends.FirstOrDefault(candidate => candidate.Id == id) is { } trend)
        {
            var text = trend.Name.Length > 0 ? trend.Name : trend.Id;
            return ([text], text);
        }

        if (model.Influences.FirstOrDefault(candidate => candidate.Id == id) is { } influence)
        {
            var text = $"{NameOf(model, influence.From)} \u2192 {NameOf(model, influence.To)}";
            return ([text], text);
        }

        return null;
    }

    private static string NameOf(GhgModel model, string trendId) =>
        model.Trends.FirstOrDefault(t => t.Id == trendId) is { Name.Length: > 0 } trend ? trend.Name : trendId;

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
