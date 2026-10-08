using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Resolves a node or a flow of a <c>.skv</c> document to a selection.</summary>
/// <remarks>
/// <para>
/// <b>The element a selection describes is the one the canvas was sent</b>: its payload comes from
/// <see cref="SankeyElementMapper"/>, so the stream and the selection never describe one entry two
/// ways.
/// </para>
/// <para>
/// <b>A placement or a finished connect gesture resolves too</b>: <c>new:x,y</c> names the node a
/// drop or a background menu is about to create, and <c>rel:from-&gt;to</c> the flow a gesture
/// proposes.
/// </para>
/// </remarks>
public sealed class SankeyContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly ISankeyDocumentStore _documents;
    private readonly SankeyElementMapper _mapper;

    public SankeyContextSourceResolver(DiagramFileRouter router, ISankeyDocumentStore documents, SankeyElementMapper mapper)
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
        ContextSource id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        cancellationToken.ThrowIfCancellationRequested();

        if (parent is null || parent.Scope != ContextScope.Hierarchy)
        {
            return Rejected("A diagram element must be selected within its diagram.");
        }

        if (_router.Route(parent.Target.ResolvedFullPath, rootPath) is not DiagramRouted routed ||
            routed.Definition.Origin != Diagram.Sankey.Origin ||
            routed.BodyPath is not { Length: > 0 } bodyPath)
        {
            return Rejected("The selected file is not a Sankey diagram.");
        }

        var model = _documents.GetOrLoad(bodyPath).Model;
        var elementId = id.ElementId.Value;

        ContextTarget Target() => new(
            ContextScope.DiagramElement,
            bodyPath,
            IsContainer: false,
            SourceId: default,
            rootPath,
            watchId,
            elementId,
            routed.Definition.Origin);

        if (GestureIds.TryParsePlacement(elementId, out _, out _) || GestureIds.TryParseRelation(elementId, out _, out _))
        {
            var proposed = GestureIds.IsPlacement(elementId) ? "New node" : "New flow";
            return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(new ContextResolvedLevel(
                id, [proposed], ContextScope.DiagramElement, Target(),
                new ContextLevelDetail { Element = new ElementDetail { Text = proposed } },
                this)));
        }

        if (Describe(model, elementId) is not { } described)
        {
            return Rejected("That is no longer in this diagram.");
        }

        (IReadOnlyList<string> path, string text) = described;

        // The client's path is checked, never trusted.
        if (clientPath.Count > 0 && !clientPath.SequenceEqual(path, StringComparer.Ordinal))
        {
            return Rejected("The path does not match the element.");
        }

        var detail = new ContextLevelDetail { Element = new ElementDetail { Text = text } };
        if (_mapper.All(model).FirstOrDefault(candidate => candidate.Id == elementId) is { } drawn)
        {
            detail.Element.ElementType = drawn.Type;
            detail.Element.Payload = new Any { TypeUrl = drawn.PayloadTypeUrl, Value = ByteString.CopyFrom(drawn.Payload.Span) };
        }

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(
            new ContextResolvedLevel(id, path, ContextScope.DiagramElement, Target(), detail, this)));
    }

    /// <inheritdoc />
    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    /// <inheritdoc />
    /// <remarks>A rename keeps the id while changing what is shown, and a removal clears the selection.</remarks>
    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(onChange);
        _ = watchId;
        _ = rootPath;

        var bodyPath = level.Target.ResolvedFullPath;
        var elementId = level.Target.ElementId;

        void OnChanged(object? sender, SankeyDocumentChangedEventArgs args)
        {
            if (string.Equals(args.Path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                onChange(Describe(_documents.GetOrLoad(bodyPath).Model, elementId)?.Path);
            }
        }

        _documents.Changed += OnChanged;
        return new Unsubscriber(() => _documents.Changed -= OnChanged);
    }

    /// <summary>What a selection of <paramref name="id"/> shows, or null when nothing has that id.</summary>
    private static (IReadOnlyList<string> Path, string Text)? Describe(SankeyModel model, string id)
    {
        if (SankeyEdits.NodeOf(model, id) is { } node)
        {
            return ([node.Label], node.Label);
        }

        if (SankeyEdits.FlowOf(model, id) is { } flow)
        {
            var text = $"{Label(model, flow.From)} → {Label(model, flow.To)}";
            return ([text], text);
        }

        return null;
    }

    private static string Label(SankeyModel model, string id) => SankeyEdits.NodeOf(model, id)?.Label ?? id;

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
