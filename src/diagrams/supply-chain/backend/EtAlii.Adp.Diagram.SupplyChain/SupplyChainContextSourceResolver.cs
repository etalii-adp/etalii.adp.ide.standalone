using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// Resolves a group, node or flow of a <c>.supply</c> document to a selection - and tells the
/// sessions what is selected, which is what the trace highlight is drawn from.
/// </summary>
/// <remarks>
/// <para>
/// <b>The element a selection describes is the one the canvas was sent</b>: its payload comes from
/// <see cref="SupplyChainElementMapper"/>, so the stream and the selection never describe one entry
/// two ways.
/// </para>
/// <para>
/// <b>A placement or a finished connect gesture resolves too</b>: <c>new:x,y</c> names the node a
/// drop or a background menu is about to create, and <c>rel:from-&gt;to</c> the flow a gesture
/// proposes. Neither is a selection of anything drawn, so neither is traced.
/// </para>
/// </remarks>
public sealed class SupplyChainContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly ISupplyChainDocumentStore _documents;
    private readonly SupplyChainElementMapper _mapper;
    private readonly SupplyChainSelections _selections;

    public SupplyChainContextSourceResolver(
        DiagramFileRouter router,
        ISupplyChainDocumentStore documents,
        SupplyChainElementMapper mapper,
        SupplyChainSelections selections)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(selections);
        _router = router;
        _documents = documents;
        _mapper = mapper;
        _selections = selections;
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
            routed.Definition.Origin != Diagram.SupplyChain.Origin ||
            routed.BodyPath is not { Length: > 0 } bodyPath)
        {
            return Rejected("The selected file is not a supply chain diagram.");
        }

        var model = _documents.GetOrLoad(bodyPath).Model;
        var elementId = id.ElementId.Value;

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

        ContextTarget Target() => new(
            ContextScope.DiagramElement,
            bodyPath,
            IsContainer: false,
            SourceId: default,
            rootPath,
            watchId,
            elementId,
            routed.Definition.Origin);
    }

    /// <inheritdoc />
    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    /// <inheritdoc />
    /// <remarks>
    /// <b>The selection lives exactly as long as this tracking does.</b> The context service calls
    /// this when a selection is recorded and disposes it when the selection moves on, so registering
    /// with <see cref="SupplyChainSelections"/> here and releasing on dispose is the trace's whole lifecycle.
    /// </remarks>
    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(onChange);
        _ = rootPath;

        var bodyPath = level.Target.ResolvedFullPath;
        var elementId = level.Target.ElementId;

        _documents.Changed += OnChanged;
        var selected = GestureIds.IsPlacement(elementId) || GestureIds.IsRelation(elementId) || elementId.Length == 0
            ? null
            : _selections.Select(watchId, bodyPath, elementId);

        return new Unsubscriber(() =>
        {
            _documents.Changed -= OnChanged;
            selected?.Dispose();
        });

        void OnChanged(object? sender, SupplyChainDocumentChangedEventArgs args)
        {
            if (!string.Equals(args.Path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Re-read: a rename keeps the id while changing what is shown, and a removal clears the selection.
            onChange(Describe(_documents.GetOrLoad(bodyPath).Model, elementId)?.Path);
        }
    }

    /// <summary>What a selection of <paramref name="id"/> shows, or null when nothing has that id.</summary>
    private static (IReadOnlyList<string> Path, string Text)? Describe(SupplyChainModel model, string id)
    {
        if (model.Groups.FirstOrDefault(candidate => candidate.Id == id) is { } group)
        {
            var text = group.Name.Length > 0 ? group.Name : group.Id;
            return ([text], text);
        }

        if (model.Nodes.FirstOrDefault(candidate => candidate.Id == id) is { } node)
        {
            var text = node.Name.Length > 0 ? node.Name : node.Id;
            return ([text], text);
        }

        if (model.Flows.FirstOrDefault(candidate => candidate.Id == id) is { } flow)
        {
            var text = flow.Product.Length > 0 ? flow.Product : $"{flow.From} → {flow.To}";
            return ([text], text);
        }

        return null;
    }

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
