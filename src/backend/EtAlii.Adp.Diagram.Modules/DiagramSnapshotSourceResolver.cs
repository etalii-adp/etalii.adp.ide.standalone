using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// Resolves an element of one diagram type to a selection, whatever the diagram is read from: what
/// every element resolver did the same way, written once (code-structure FR-032).
/// </summary>
/// <remarks>
/// <para>
/// <b>A module says what its elements are, and where they are read from.</b> It loads a snapshot of
/// the diagram behind a selected file (<see cref="LoadAsync"/>), follows it (<see cref="Follow"/>),
/// describes an element and maps the one element the canvas was sent. Checking that the selection
/// sits inside a file of this type, resolving a placement or a proposed relation, checking the
/// client's path, building the payload and the target, and tracking a selection are here.
/// </para>
/// <para>
/// <b>Most modules read one document of a store</b>, and derive from
/// <see cref="DiagramElementSourceResolver{TBody, TModel}"/>, which loads and follows it for them. A
/// diagram drawn from a whole folder, such as an Ansible project, derives from this directly: its
/// selections are made inside its registration, and it loads and follows the folder itself.
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
/// <typeparam name="TSnapshot">What the module reads a selected diagram into, once per resolution.</typeparam>
public abstract class DiagramSnapshotSourceResolver<TSnapshot> : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;

    /// <param name="router">Says which diagram type a selected file is.</param>
    protected DiagramSnapshotSourceResolver(DiagramFileRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        _router = router;
    }

    /// <summary>The diagram type whose elements this resolves.</summary>
    protected abstract ToolOrigin Origin { get; }

    /// <summary>The sentences a selection is refused with, and the names of a proposed element and relation.</summary>
    protected abstract DiagramElementTexts Texts { get; }

    /// <inheritdoc />
    public bool CanResolve(ContextSource source) => source.SourceCase == ContextSource.SourceOneofCase.ElementId;

    /// <inheritdoc />
    public async ValueTask<ContextLevelResolution> ResolveAsync(
        ShortGuid watchId,
        ProjectRoot root,
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

        if (_router.Route(parent.Target.ResolvedFullPath, root.FullPath) is not DiagramRouted routed ||
            routed.Definition.Origin != Origin ||
            routed.BodyPath is not { Length: > 0 } bodyPath)
        {
            return Rejected(Texts.NotThisType);
        }

        var elementId = id.ElementId.Value;
        var target = ContextTarget.ForElement(bodyPath, elementId, root, watchId, routed.Definition.Origin);

        if (GestureIds.TryParsePlacement(elementId, out _, out _) || GestureIds.TryParseRelation(elementId, out _, out _))
        {
            return Proposed(id, GestureIds.IsPlacement(elementId) ? Texts.NewElement : Texts.NewRelation, target);
        }

        var snapshot = await LoadAsync(bodyPath, cancellationToken);
        if (ProposedOf(snapshot, elementId) is { } other)
        {
            return Proposed(id, other, target);
        }

        if (Describe(snapshot, elementId) is not { } described)
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

        var detail = new ContextLevelDetail { Element = new ElementDetail { Text = text, HasChildren = HasChildren(snapshot, elementId) } };
        if (Drawn(snapshot, elementId) is { } drawn)
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
    public IDisposable Track(ShortGuid watchId, ProjectRoot root, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(onChange);
        _ = root;

        var bodyPath = level.Target.ResolvedFullPath;
        var elementId = level.Target.ElementId;
        return Follow(watchId, bodyPath, elementId, () =>
        {
            if (Current(bodyPath) is { } snapshot)
            {
                onChange(Describe(snapshot, elementId)?.Path);
            }
        });
    }

    /// <summary>The diagram whose body, as the router names it, is at <paramref name="bodyPath"/>.</summary>
    protected abstract ValueTask<TSnapshot> LoadAsync(string bodyPath, CancellationToken cancellationToken);

    /// <summary>
    /// The diagram whose body is at <paramref name="bodyPath"/> as it stands when <see cref="Follow"/>
    /// reports a change, or null when none is held. A selection is only tracked once it was resolved,
    /// so the diagram was loaded, and a change is reported once the new diagram is in place: a module
    /// whose store keeps what it read answers from that, and reads nothing.
    /// </summary>
    protected abstract TSnapshot? Current(string bodyPath);

    /// <summary>What a selection of <paramref name="elementId"/> in <paramref name="snapshot"/> shows, or null when nothing has that id.</summary>
    protected abstract (IReadOnlyList<string> Path, string Text)? Describe(TSnapshot snapshot, string elementId);

    /// <summary>The element <paramref name="elementId"/> as the canvas was sent it, or null when none is drawn.</summary>
    protected abstract DiagramElement? Drawn(TSnapshot snapshot, string elementId);

    /// <summary>
    /// What <paramref name="elementId"/> is called when it names a target of the module's own besides
    /// a drop and a connect gesture, such as the diagram itself or a group of one element; null when it
    /// names none. Such a target, like a gesture, is never selected, tracked or written. None by default.
    /// </summary>
    protected virtual string? ProposedOf(TSnapshot snapshot, string elementId) => null;

    /// <summary>Whether <paramref name="elementId"/> has children, for a diagram whose elements form a tree; none by default.</summary>
    protected virtual bool HasChildren(TSnapshot snapshot, string elementId) => false;

    /// <summary>
    /// Calls <paramref name="changed"/> whenever the diagram whose body is at <paramref name="bodyPath"/>
    /// changes, until the result is disposed. <paramref name="watchId"/> and <paramref name="elementId"/>
    /// are for a module that also follows what each connection has selected, as supply chain does for
    /// its trace.
    /// </summary>
    protected abstract IDisposable Follow(ShortGuid watchId, string bodyPath, string elementId, Action changed);

    private ContextLevelResolution Proposed(ContextSource id, string text, ContextTarget target) =>
        Resolved(new ContextResolvedLevel(
            id,
            [text],
            ContextScope.DiagramElement,
            target,
            new ContextLevelDetail { Element = new ElementDetail { Text = text } },
            this));

    private static ResolvedContextLevel Resolved(ContextResolvedLevel level) => new(level);

    private static RejectedContextLevel Rejected(string reason) => new(reason);
}

/// <summary>What a diagram type's resolver says in its own words.</summary>
/// <param name="NotThisType">The refusal for a selection inside a file of another type, such as "The selected file is not a Sankey diagram."</param>
/// <param name="Gone">The refusal for an id the document no longer has, such as "That is no longer in this diagram."</param>
/// <param name="NewElement">The name of the element a placement is about to create, such as "New node".</param>
/// <param name="NewRelation">The name of the relation a connect gesture proposes, such as "New flow".</param>
public sealed record DiagramElementTexts(string NotThisType, string Gone, string NewElement, string NewRelation);
