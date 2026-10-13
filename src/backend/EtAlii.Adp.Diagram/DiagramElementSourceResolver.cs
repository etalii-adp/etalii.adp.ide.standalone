using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// Resolves an element of one diagram type to a selection: what every element resolver did the same
/// way, written once (code-structure FR-032).
/// </summary>
/// <remarks>
/// <para>
/// <b>A module says what its elements are, and nothing else.</b> It loads its model, describes an
/// element, maps the one element the canvas was sent and follows its document. Checking that the
/// selection sits inside a file of this type, resolving a placement or a proposed relation, checking
/// the client's path, building the payload and the target, and tracking a selection are here.
/// </para>
/// <para>
/// <b>The element a selection describes is the one the canvas was sent</b>: its payload comes from
/// the module's mapping (<see cref="Drawn"/>), so the stream and the selection never describe one
/// entry two ways.
/// </para>
/// <para>
/// <b>A placement or a finished connect gesture resolves too</b>: <c>new:x,y</c> names the element a
/// drop or a background menu is about to create, and <c>rel:from-&gt;to</c> the relation a gesture
/// proposes. Each resolves to a target the action provider reads back out.
/// </para>
/// </remarks>
/// <typeparam name="TModel">What the module's parser reads from a body.</typeparam>
public abstract class DiagramElementSourceResolver<TModel> : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;

    protected DiagramElementSourceResolver(DiagramFileRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        _router = router;
    }

    /// <summary>The diagram type whose elements this resolves.</summary>
    protected abstract DiagramOrigin Origin { get; }

    /// <summary>The sentences a selection is refused with, and the names of a proposed element and relation.</summary>
    protected abstract DiagramElementTexts Texts { get; }

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
        ArgumentNullException.ThrowIfNull(clientPath);
        cancellationToken.ThrowIfCancellationRequested();

        // An element is only ever selected inside a diagram: with no file level above it there is
        // nothing to verify it against, and an unverifiable selection is never recorded.
        if (parent is null || parent.Scope != ContextScope.Hierarchy)
        {
            return Rejected("A diagram element must be selected within its diagram.");
        }

        if (_router.Route(parent.Target.ResolvedFullPath, rootPath) is not DiagramRouted routed ||
            routed.Definition.Origin != Origin ||
            routed.BodyPath is not { Length: > 0 } bodyPath)
        {
            return Rejected(Texts.NotThisType);
        }

        var elementId = id.ElementId.Value;
        var target = ContextTarget.ForElement(bodyPath, elementId, rootPath, watchId, routed.Definition.Origin);

        if (GestureIds.TryParsePlacement(elementId, out _, out _) || GestureIds.TryParseRelation(elementId, out _, out _))
        {
            var proposed = GestureIds.IsPlacement(elementId) ? Texts.NewElement : Texts.NewRelation;
            return Resolved(new ContextResolvedLevel(
                id,
                [proposed],
                ContextScope.DiagramElement,
                target,
                new ContextLevelDetail { Element = new ElementDetail { Text = proposed } },
                this));
        }

        var model = Load(bodyPath);
        if (Describe(model, elementId) is not { } described)
        {
            return Rejected(Texts.Gone);
        }

        (IReadOnlyList<string> path, string text) = described;

        // The client's path is checked, never trusted: it is what the client believes it selected,
        // and the id is what it actually selected.
        if (clientPath.Count > 0 && !clientPath.SequenceEqual(path, StringComparer.Ordinal))
        {
            return Rejected("The path does not match the element.");
        }

        var detail = new ContextLevelDetail { Element = new ElementDetail { Text = text } };
        if (Drawn(model, elementId) is { } drawn)
        {
            detail.Element.ElementType = drawn.Type;
            detail.Element.Payload = new Any { TypeUrl = drawn.PayloadTypeUrl, Value = ByteString.CopyFrom(drawn.Payload.Span) };
        }

        return Resolved(new ContextResolvedLevel(id, path, ContextScope.DiagramElement, target, detail, this));
    }

    /// <inheritdoc />
    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    /// <inheritdoc />
    /// <remarks>A rename keeps the id while changing what is shown, and a removal clears the selection.</remarks>
    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(onChange);
        _ = rootPath;

        var bodyPath = level.Target.ResolvedFullPath;
        var elementId = level.Target.ElementId;
        return Follow(watchId, bodyPath, elementId, () => onChange(Describe(Load(bodyPath), elementId)?.Path));
    }

    /// <summary>The model of the document whose body is at <paramref name="bodyPath"/>.</summary>
    protected abstract TModel Load(string bodyPath);

    /// <summary>What a selection of <paramref name="elementId"/> shows, or null when nothing has that id.</summary>
    protected abstract (IReadOnlyList<string> Path, string Text)? Describe(TModel model, string elementId);

    /// <summary>The element <paramref name="elementId"/> as the canvas was sent it, or null when none is drawn.</summary>
    protected abstract DiagramElement? Drawn(TModel model, string elementId);

    /// <summary>
    /// Calls <paramref name="changed"/> whenever the document at <paramref name="bodyPath"/> changes,
    /// until the result is disposed. <paramref name="watchId"/> and <paramref name="elementId"/> are
    /// for a module that also follows what each connection has selected, as supply chain does for
    /// its trace.
    /// </summary>
    protected abstract IDisposable Follow(ShortGuid watchId, string bodyPath, string elementId, Action changed);

    /// <summary>
    /// <see cref="Follow"/> for a module whose document lives in an <see cref="IDiagramDocumentStore{TBody, TModel}"/>:
    /// <paramref name="changed"/> on every change to <paramref name="bodyPath"/>, until disposed.
    /// </summary>
    protected static IDisposable FollowStore<TBody>(IDiagramDocumentStore<TBody, TModel> documents, string bodyPath, Action changed)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(changed);

        documents.Changed += OnChanged;
        return Disposable.From(() => documents.Changed -= OnChanged);

        void OnChanged(object? sender, DocumentChangedEventArgs args)
        {
            if (string.Equals(args.Path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                changed();
            }
        }
    }

    private static ValueTask<ContextLevelResolution> Resolved(ContextResolvedLevel level) =>
        ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));
}

/// <summary>What a diagram type's resolver says in its own words.</summary>
/// <param name="NotThisType">The refusal for a selection inside a file of another type, such as "The selected file is not a Sankey diagram."</param>
/// <param name="Gone">The refusal for an id the document no longer has, such as "That is no longer in this diagram."</param>
/// <param name="NewElement">The name of the element a placement is about to create, such as "New node".</param>
/// <param name="NewRelation">The name of the relation a connect gesture proposes, such as "New flow".</param>
public sealed record DiagramElementTexts(string NotThisType, string Gone, string NewElement, string NewRelation);
