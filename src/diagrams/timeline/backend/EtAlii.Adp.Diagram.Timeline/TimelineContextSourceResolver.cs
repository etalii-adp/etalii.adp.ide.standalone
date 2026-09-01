using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Makes a timeline element - or a connection - selectable: resolves an <c>element_id</c>
/// against the document named by the enclosing level and verifies the id really is in it.
/// </summary>
/// <remarks>
/// Every diagram type answers for <c>element_id</c>, and none can tell from the id alone
/// whether the element is one of its own - only the file above it says that. So this refuses a
/// file that is not a <c>.tml</c> and the selection resolver moves on to the next resolver,
/// which is what lets every module's element resolver coexist.
/// </remarks>
public sealed class TimelineContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly ITimelineDocumentStore _documents;
    private readonly TimelineElementMapper _mapper;

    /// <summary>Creates the resolver over the router and the one document store.</summary>
    public TimelineContextSourceResolver(
        DiagramFileRouter router,
        ITimelineDocumentStore documents,
        TimelineElementMapper mapper)
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

        // An element is only ever selected inside a diagram: with no file level above it there
        // is nothing to verify it against, and an unverifiable selection is never recorded.
        if (parent is null || parent.Scope != ContextScope.Hierarchy)
        {
            return Rejected("A diagram element must be selected within its diagram.");
        }

        if (_router.Route(parent.Target.ResolvedFullPath, rootPath) is not DiagramRouted routed ||
            routed.Definition.Origin != Diagram.Timeline.Origin ||
            routed.BodyPath is not { Length: > 0 } bodyPath)
        {
            return Rejected("The selected file is not a timeline.");
        }

        var model = _documents.GetOrLoad(bodyPath).Model;
        var elementId = id.ElementId.Value;

        // A placement id names the element about to exist at a position - the way a drop or a
        // relation-to-empty-space carries where it landed through a channel that has one element
        // id per call and no other field. It resolves like any element, to a target the action
        // provider reads the position back out of; it lives for one ExecuteAction and is never
        // selected, tracked or written anywhere.
        if (TimelineNewPlacement.TryParse(elementId, out _, out _) ||
            TimelineRelationGesture.TryParse(elementId, out _, out _))
        {
            return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(new ContextResolvedLevel(
                source,
                id,
                ["New element"],
                ContextScope.DiagramElement,
                new ContextTarget(
                    ContextScope.DiagramElement,
                    bodyPath,
                    IsContainer: false,
                    SourceId: default,
                    rootPath,
                    watchId,
                    elementId),
                new ContextLevelDetail { Element = new ElementDetail { Text = "New element" } },
                this)));
        }

        var described = Describe(model, elementId);
        if (described is null)
        {
            return Rejected("That element is no longer on this timeline.");
        }

        var (path, text) = described.Value;

        // The client's path is checked, never trusted: it is what the client believes it
        // selected, and the id is what it actually selected.
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

        // The same payload the canvas already has, taken from the mapper rather than assembled
        // here, so the selection and the stream can never describe one element two ways.
        var element = _mapper.Elements(model).FirstOrDefault(candidate => candidate.Id == elementId);
        if (element is not null)
        {
            detail.Element.ElementType = element.Type;
            detail.Element.Payload = new Any
            {
                TypeUrl = element.PayloadTypeUrl,
                Value = ByteString.CopyFrom(element.Payload.Span),
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
                elementId),
            detail,
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    /// <summary>Nothing nests inside a timeline element for selection purposes.</summary>
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

        void OnChanged(object? sender, TimelineDocumentChangedEventArgs args)
        {
            if (!string.Equals(args.Path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Re-read rather than carrying the previous reading: a rename keeps the id while
            // changing everything shown, and a removal clears the selection.
            onChange(Describe(args.Model, elementId)?.Path);
        }

        _documents.Changed += OnChanged;
        return new TimelineUnsubscriber(() => _documents.Changed -= OnChanged);
    }

    /// <summary>
    /// The path and display text an id resolves to - an element's, or a connection's, since both
    /// live on the same stream and are selected through the same channel.
    /// </summary>
    private static (IReadOnlyList<string> Path, string Text)? Describe(TimelineModel model, string id)
    {
        var element = TimelineEdits.ElementOf(model, id);
        if (element is not null)
        {
            var text = element.Label.Length > 0 ? element.Label : element.Id;
            return ([text], text);
        }

        var connection = TimelineEdits.ConnectionOf(model, id);
        if (connection is not null)
        {
            var text = connection.Label.Length > 0
                ? connection.Label
                : $"{connection.From} → {connection.To}";
            return ([text], text);
        }

        return null;
    }

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));
}
