using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Makes a Wardley element selectable: resolves an <c>element_id</c> against the map named by
/// the enclosing level, verifies the element really is on it, and fills in the detail a
/// consumer shows (Requirements 11.1-11.3). Registering this is the whole of it; nothing inside
/// the context service changes.
/// </summary>
/// <remarks>
/// <para>
/// Every diagram type answers for <c>element_id</c>, and none of them can tell from the id
/// alone whether the element is one of its own - only the file above it says that. So this
/// refuses a file that is not a Wardley map and the selection resolver moves on to the next
/// resolver, which is what makes several element resolvers able to coexist.
/// </para>
/// <para>
/// The diagram element is on the element stream but is <b>not</b> selectable here: it is the map
/// itself rather than a thing the map contains, and the map is already selected as its file.
/// </para>
/// <para>
/// <b>It stays off the shared resolver</b> (code-structure FR-032): a Wardley element's id is the
/// store's identity for it, kept per document beside the map, and the shared resolver describes an
/// element from the model alone.
/// </para>
/// </remarks>
public sealed class WardleyContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly IWardleyDocumentStore _documents;
    private readonly WardleyElementMapper _mapper;

    public WardleyContextSourceResolver(
        DiagramFileRouter router,
        IWardleyDocumentStore documents,
        WardleyElementMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);

        _router = router;
        _documents = documents;
        _mapper = mapper;
    }

    public bool CanResolve(ContextSource source) => source.SourceCase == ContextSource.SourceOneofCase.ElementId;

    public async ValueTask<ContextLevelResolution> ResolveAsync(
        ShortGuid watchId,
        ProjectRoot root,
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

        if (_router.Route(parent.Target.ResolvedFullPath, root.FullPath) is not DiagramRouted routed ||
            routed.Definition.Origin != Diagram.WardleyMap.Origin ||
            routed.BodyPath is not { Length: > 0 } bodyPath)
        {
            return Rejected("The selected file is not a Wardley map.");
        }

        var map = (await _documents.GetOrLoadAsync(bodyPath, cancellationToken)).Model;
        var identities = _documents.Identities(bodyPath);
        var elementId = id.ElementId.Value;

        var description = WardleyElementDescriptions.Of(map, identities, elementId);
        if (description is null)
        {
            return Rejected("That element is no longer on this map.");
        }

        // The client's path is checked, never trusted: it is what the client believes it
        // selected, and the id is what it actually selected.
        if (clientPath.Count > 0 && !clientPath.SequenceEqual(description.Path, StringComparer.Ordinal))
        {
            return Rejected("The path does not match the element.");
        }

        var detail = new ContextLevelDetail
        {
            Element = new ElementDetail
            {
                Text = description.Text,
                HasChildren = description.HasChildren,
                Folded = false,
                Linked = false,
            },
        };

        // The same payload the canvas already has, so a consumer holding this module can show
        // the element's kind, its coordinates and its evolution stage without asking again
        // (Requirement 11.3). Taken from the mapper rather than assembled here, so the
        // selection and the stream can never describe one element two ways.
        var element = _mapper
            .Elements(map, identities)
            .FirstOrDefault(candidate => candidate.Id == elementId);
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
            id,
            description.Path,
            ContextScope.DiagramElement,
            new ContextTarget(
                ContextScope.DiagramElement,
                bodyPath,
                IsContainer: false,
                SourceId: default,
                root,
                watchId,
                elementId,
                routed.Definition.Origin),
            detail,
            // The level carries its own resolver, which is how the selection store re-resolves
            // it after the document changes.
            this);

        return new ResolvedContextLevel(level);
    }

    /// <summary>
    /// Nothing nests inside an element for selection purposes - not even a pipeline's children.
    /// A child is selected in its own right, carrying its parent in its path; selecting one
    /// <em>within</em> the other would be a chain the canvas never produces.
    /// </summary>
    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    /// <summary>
    /// Re-resolves after every change to the document: a rename changes the path, a removal
    /// clears the selection. One document backs every connection that has the map open, so a
    /// change made through any of them re-resolves this one.
    /// </summary>
    public IDisposable Track(
        ShortGuid watchId,
        ProjectRoot root,
        ContextResolvedLevel level,
        Action<IReadOnlyList<string>?> onChange)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(onChange);
        _ = watchId;
        _ = root;

        var bodyPath = level.Target.ResolvedFullPath;
        var elementId = level.Target.ElementId;

        _documents.Changed += OnChanged;
        return new CallbackDisposable(() => _documents.Changed -= OnChanged);

        void OnChanged(object? sender, DocumentChangedEventArgs args)
        {
            if (!string.Equals(args.Path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // The map the store now holds rather than the previous reading: the change may have been
            // an external edit, and a rename keeps the id while changing everything shown. The store
            // holds the new map by the time it reports the change, so nothing is read here.
            if (_documents.Get(bodyPath) is not { } entry)
            {
                return;
            }

            var description = WardleyElementDescriptions.Of(entry.Model, _documents.Identities(bodyPath), elementId);
            onChange(description?.Path);
        }
    }

    private static RejectedContextLevel Rejected(string reason) => new(reason);
}
