using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Hierarchy;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Makes an Ansible node selectable: resolves an <c>element_id</c> against the project named by
/// the enclosing selection level, verifies the node really is in that project, and fills in the
/// detail a consumer shows (Requirement 8.2).
/// </summary>
/// <remarks>
/// Registering this is the whole of it - the context service itself is untouched, which is
/// tech.md's rule that a new selectable thing is one resolver and never a bespoke RPC.
/// </remarks>
public sealed class AnsibleContextSourceResolver : IContextSourceResolver
{
    private readonly DiagramFileRouter _router;
    private readonly IAnsibleProjectStore _store;

    public AnsibleContextSourceResolver(DiagramFileRouter router, IAnsibleProjectStore store)
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
            origin != Diagram.AnsibleStructure.Origin)
        {
            // Not ours. Another type's resolver answers for its own elements; saying so plainly
            // is how several resolvers share one ContextSource member without fighting.
            return Rejected("The selected file is not an Ansible structure diagram.");
        }

        var folder = FolderOf(parent.Target.ResolvedFullPath);
        if (folder is null)
        {
            return Rejected("The Ansible project folder could not be resolved.");
        }

        var project = _store.GetOrLoad(folder);
        var graph = AnsibleGraph.Derive(project);

        var elementId = id.ElementId.Value;
        var node = graph.Node(elementId);
        if (node is null)
        {
            // An edge is selectable too, and it answers as its declaring side (Requirement 8.3).
            var edge = graph.Edges.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
            if (edge is null)
            {
                return Rejected("Unknown element.");
            }

            return Resolve(
                watchId, rootPath, source, id, clientPath, folder, elementId,
                Segments(edge.Directive.DeclaredIn),
                new ContextLevelDetail
                {
                    Element = new ElementDetail
                    {
                        Text = $"{DirectiveLabel(edge)} {edge.Directive.Target}",
                        HasChildren = false,
                    },
                });
        }

        return Resolve(
            watchId, rootPath, source, id, clientPath, folder, elementId,
            Segments(node.RelativePath),
            new ContextLevelDetail
            {
                Element = new ElementDetail
                {
                    Text = node.Name,
                    // Nothing on this diagram folds, so nothing has children for selection
                    // purposes and nothing is ever reported folded.
                    HasChildren = false,
                },
            });
    }

    /// <summary>Nothing nests inside an Ansible node; a role's task files are their own selections.</summary>
    public ContextNesting NestingOf(ContextResolvedLevel level) => ContextNesting.NotNestable;

    /// <summary>
    /// Re-resolves the element after every change to its folder: a deleted role clears the
    /// selection, a renamed file changes the path (Requirement 8.2).
    /// </summary>
    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(onChange);

        // The target's path is already the folder - Resolve put it there, because for this type
        // the folder IS what an element resolved within. Taking GetDirectoryName of it here
        // would silently watch the folder above and never hear a thing; that is exactly the bug
        // ADeletedRole_ClearsTheSelection caught.
        var folder = IoPath.GetFullPath(level.Target.ResolvedFullPath);
        var elementId = level.Target.ElementId;
        var lastPath = level.RelativePath;
        var disposed = false;

        void OnChanged(object? sender, AnsibleProjectChangedEventArgs args)
        {
            if (disposed ||
                !string.Equals(IoPath.GetFullPath(args.FolderPath), folder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var graph = AnsibleGraph.Derive(args.Project);
            var node = graph.Node(elementId);
            if (node is null)
            {
                var edge = graph.Edges.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
                if (edge is null)
                {
                    onChange(null); // Gone from the folder; the selection goes with it.
                    return;
                }

                Announce(Segments(edge.Directive.DeclaredIn));
                return;
            }

            Announce(Segments(node.RelativePath));
        }

        void Announce(string[] current)
        {
            if (!current.SequenceEqual(lastPath, StringComparer.Ordinal))
            {
                lastPath = current;
                onChange(current);
            }
        }

        _store.Changed += OnChanged;
        return new AnsibleNodeSubscription(() =>
        {
            disposed = true;
            _store.Changed -= OnChanged;
        });
    }

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
        ContextLevelDetail detail)
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
            detail,
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    /// <summary>The directive as the file spells it, for an edge's own label.</summary>
    private static string DirectiveLabel(AnsibleEdge edge) => edge.Directive.Kind switch
    {
        AnsibleDirectiveKind.Roles => "roles:",
        AnsibleDirectiveKind.ImportRole => "import_role:",
        AnsibleDirectiveKind.IncludeRole => "include_role:",
        AnsibleDirectiveKind.ImportPlaybook => "import_playbook:",
        AnsibleDirectiveKind.ImportTasks => "import_tasks:",
        AnsibleDirectiveKind.IncludeTasks => "include_tasks:",
        AnsibleDirectiveKind.Dependency => "dependencies:",
        _ => "",
    };

    private static string[] Segments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));
}
