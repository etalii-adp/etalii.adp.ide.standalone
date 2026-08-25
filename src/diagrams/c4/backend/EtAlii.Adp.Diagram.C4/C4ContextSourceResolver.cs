using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Makes a C4 element selectable: resolves an <c>element_id</c> against the model named by the
/// enclosing selection level, verifies the element really is in it, and fills in the detail a
/// consumer shows. Registering this is the whole of it; the context service is untouched
/// (c4-diagrams Requirement 13.2).
/// </summary>
/// <remarks>
/// Registered once for the whole family rather than once per type. Which C4 type the file is
/// does not change what an element *is*, and the resolver only has to agree that the file is a
/// C4 document at all.
/// </remarks>
public sealed class C4ContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly IC4DocumentStore _documents;

    public C4ContextSourceResolver(DiagramFileRouter router, IC4DocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(documents);
        _router = router;
        _documents = documents;
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
        cancellationToken.ThrowIfCancellationRequested();

        // An element is only ever selected inside a diagram: with no file level above it there
        // is nothing to verify it against.
        if (parent is null || parent.Scope != ContextScope.Hierarchy)
        {
            return Rejected("A diagram element must be selected within its diagram.");
        }

        if (_router.Route(parent.Target.ResolvedFullPath, rootPath) is not DiagramRouted routed ||
            routed.Definition.Origin.Vendor != "c4")
        {
            return Rejected("The selected file is not a C4 diagram.");
        }

        var bodyPath = routed.BodyPath!;
        var workspace = _documents.WorkspaceOf(bodyPath);
        var elementId = id.ElementId.Value;
        var element = workspace.Find(elementId);
        if (element is null)
        {
            // A relationship is selectable too, and carries no name of its own.
            var relationship = workspace.Relationships.FirstOrDefault(candidate => candidate.Id == elementId);
            return relationship is null
                ? Rejected("Unknown element.")
                : Resolve(watchId, rootPath, source, id, bodyPath, elementId, RelationshipDetail(workspace, relationship), []);
        }

        // The path is the element's chain of names from the top of the model, relative to the
        // file level; the client's version is checked, never trusted.
        var relativePath = PathOf(workspace, element);
        if (clientPath.Count > 0 && !clientPath.SequenceEqual(relativePath, StringComparer.Ordinal))
        {
            return Rejected("The path does not match the element.");
        }

        var detail = new ContextLevelDetail
        {
            Element = new ElementDetail
            {
                Text = element.Name,
                HasChildren = workspace.Elements.Any(candidate =>
                    string.Equals(candidate.ParentId, element.Id, StringComparison.OrdinalIgnoreCase)),
                Folded = false,
                Linked = false,
            },
        };

        return Resolve(watchId, rootPath, source, id, bodyPath, elementId, detail, relativePath);
    }

    /// <summary>Nothing nests inside an element for selection purposes.</summary>
    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    /// <summary>
    /// Re-resolves after every change to the model: a rename changes the path, a deletion
    /// clears the selection. One document may back several open views, so a change through any
    /// of them re-resolves this one.
    /// </summary>
    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(onChange);

        var bodyPath = level.Target.ResolvedFullPath;
        var elementId = level.Target.ElementId;

        void OnChanged(object? sender, C4DocumentChangedEventArgs args)
        {
            if (!string.Equals(args.Path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var element = args.Workspace.Find(elementId);
            onChange(element is null ? null : PathOf(args.Workspace, element));
        }

        _documents.Changed += OnChanged;
        return new C4ElementUnsubscriber(() => _documents.Changed -= OnChanged);
    }

    /// <summary>The element's chain of names from the top of the model - what identifies it to a human.</summary>
    private static IReadOnlyList<string> PathOf(C4Workspace workspace, C4Element element)
    {
        var names = new List<string>();
        var current = element;
        while (current is not null)
        {
            names.Insert(0, current.Name.Length > 0 ? current.Name : current.Id);
            current = current.ParentId is { } parentId ? workspace.Find(parentId) : null;
        }

        return names;
    }

    private static ContextLevelDetail RelationshipDetail(C4Workspace workspace, C4Relationship relationship)
    {
        var source = workspace.Find(relationship.SourceId)?.Name ?? relationship.SourceId;
        var destination = workspace.Find(relationship.DestinationId)?.Name ?? relationship.DestinationId;
        return new ContextLevelDetail
        {
            Element = new ElementDetail
            {
                Text = relationship.Description.Length > 0
                    ? $"{source} -> {destination}: {relationship.Description}"
                    : $"{source} -> {destination}",
                HasChildren = false,
                Folded = false,
                Linked = false,
            },
        };
    }

    private ValueTask<ContextLevelResolution> Resolve(
        ShortGuid watchId,
        string rootPath,
        ContextSelectionSource source,
        ContextSource id,
        string bodyPath,
        string elementId,
        ContextLevelDetail detail,
        IReadOnlyList<string> relativePath)
    {
        var level = new ContextResolvedLevel(
            source,
            id,
            relativePath,
            ContextScope.DiagramElement,
            new ContextTarget(ContextScope.DiagramElement, bodyPath, IsContainer: false, SourceId: default, rootPath, watchId, elementId),
            detail,
            // The level carries its own resolver, which is how the selection store re-resolves
            // it after the document changes.
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));

}
