using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Resolves hierarchy entry ids for the context service, going only through the
/// connection's own <see cref="HierarchyModel"/> - so an id another connection handed
/// out resolves to nothing here, and containment is enforced in exactly one place.
/// Folders contain entries; a file contains nothing of this kind.
/// </summary>
public sealed class HierarchyContextSourceResolver : IContextSourceResolver
{
    private readonly IHierarchyModelStore _hierarchyModelStore;
    private readonly ToolRouter _tools;
    private readonly DiagramFileRouter _diagrams;

    /// <param name="hierarchyModelStore">The hierarchy model store.</param>
    /// <param name="tools">
    /// Says which tool serves a file: whether it may contain a selected element, and which canvas
    /// an activation opens, an editor's included (modular-text-editors Requirement 5.2).
    /// </param>
    /// <param name="diagrams">Names the type of a registration that no deployed tool serves, so its tab can say so.</param>
    public HierarchyContextSourceResolver(IHierarchyModelStore hierarchyModelStore, ToolRouter tools, DiagramFileRouter diagrams)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(diagrams);
        _hierarchyModelStore = hierarchyModelStore;
        _tools = tools;
        _diagrams = diagrams;
    }

    public bool CanResolve(ContextSource source) => source.SourceCase == ContextSource.SourceOneofCase.EntryId;

    public ValueTask<ContextLevelResolution> ResolveAsync(
        ShortGuid watchId,
        string rootPath,
        ContextSource id,
        IReadOnlyList<string> clientPath,
        ContextResolvedLevel? parent,
        CancellationToken cancellationToken)
    {
        var model = _hierarchyModelStore.GetOrCreate(watchId, ProjectRoot.From(rootPath));
        var entryId = (ShortGuid)id.EntryId;
        if (!model.TryResolvePath(entryId, out var fullPath, out var isFolder))
        {
            return Rejected("Unknown entry on this connection.");
        }

        var basePath = rootPath;
        if (parent is not null)
        {
            if (!parent.Target.IsContainer || !IsInside(parent.Target.ResolvedFullPath, fullPath))
            {
                return Rejected("The entry is not inside its parent.");
            }

            basePath = parent.Target.ResolvedFullPath;
        }

        var relativePath = RelativeSegments(basePath, fullPath);
        if (clientPath.Count > 0 && !clientPath.SequenceEqual(relativePath, StringComparer.Ordinal))
        {
            return Rejected("The path does not match the entry.");
        }

        var detail = new ContextLevelDetail
        {
            Entry = new EntryDetail
            {
                Kind = isFolder ? EntryKind.Folder : EntryKind.File,
                Available = IsAvailable(fullPath, isFolder),
                // The one word the workspace needs to open the right canvas: which diagram
                // this file is, said by the same routing that opens it - so the client never
                // grows a file-type table (diagram-workspace-tabs Requirement 1).
                DiagramMimeType = isFolder ? "" : DiagramMimeTypeOf(fullPath),
            },
        };

        var level = new ContextResolvedLevel(
            id,
            relativePath,
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, fullPath, isFolder, entryId, rootPath, watchId),
            detail,
            this);

        return ValueTask.FromResult<ContextLevelResolution>(new ResolvedContextLevel(level));
    }

    /// <summary>
    /// A folder contains entries. A file contains nothing of this kind - unless it is a
    /// diagram, which contains elements: a node selected on the canvas nests under its
    /// .adp file (mindmap-diagram Requirement 10.1), resolved by the diagram type's own
    /// resolver rather than this one. A designer's file contains what its designer lets be
    /// selected in it, a row of a table say, resolved by the designer type's resolver likewise.
    /// </summary>
    public ContextNesting NestingOf(ContextResolvedLevel level) =>
        level.Target.IsContainer || _tools.Route(level.Target.ResolvedFullPath) is DiagramRouted or DesignerRouted
            ? ContextNesting.Contained
            : ContextNesting.NotNestable;

    /// <summary>
    /// Any rename or removal in the model may have moved the tracked entry - its own, or
    /// an ancestor folder's - so each one simply re-resolves the id and reports only a
    /// real difference. Re-resolving beats string-rewriting: the model already knows.
    /// </summary>
    public IDisposable Track(ShortGuid watchId, string rootPath, ContextResolvedLevel level, Action<IReadOnlyList<string>?> onChange)
    {
        var model = _hierarchyModelStore.GetOrCreate(watchId, ProjectRoot.From(rootPath));
        var entryId = (ShortGuid)level.Id.EntryId;
        var basePath = BaseOf(level);
        var lastPath = level.RelativePath;
        var disposed = false;

        model.EntryChanged += OnEntryChanged;
        return new HierarchyEntrySubscription(() =>
        {
            disposed = true;
            model.EntryChanged -= OnEntryChanged;
        });

        void OnEntryChanged(HierarchyEntryChange change)
        {
            if (disposed || change is not (HierarchyEntryRenamed or HierarchyEntryRemoved))
            {
                return;
            }

            if (!model.TryResolvePath(entryId, out var fullPath, out _))
            {
                onChange(null);
                return;
            }

            var currentPath = RelativeSegments(basePath, fullPath);
            if (!currentPath.SequenceEqual(lastPath, StringComparer.Ordinal))
            {
                lastPath = currentPath;
                onChange(currentPath);
            }
        }
    }

    private static ValueTask<ContextLevelResolution> Rejected(string reason) =>
        ValueTask.FromResult<ContextLevelResolution>(new RejectedContextLevel(reason));

    private static bool IsInside(string parentFullPath, string fullPath) => PathContainment.IsInside(parentFullPath, fullPath);

    private static string[] RelativeSegments(string basePath, string fullPath) =>
        IoPath.GetRelativePath(basePath, fullPath)
            .Split([IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The folder a level's relative path is relative to: its own location minus its own segments.</summary>
    private static string BaseOf(ContextResolvedLevel level)
    {
        var path = level.Target.ResolvedFullPath;
        for (var i = 0; i < level.RelativePath.Count; i++)
        {
            path = IoPath.GetDirectoryName(path) ?? path;
        }

        return path;
    }

    private static bool IsAvailable(string fullPath, bool isFolder) =>
        isFolder ? Directory.Exists(fullPath) : File.Exists(fullPath);

    /// <summary>
    /// The type of tool a file opens in, for the selection detail, as <see cref="ToolRouter"/>
    /// decides it (code-structure FR-058; SC-015): a diagram's or a designer's origin, or
    /// <c>editor/&lt;id&gt;</c> for an editor, whose tab keys on the same field. A registration naming
    /// a type no deployed tool serves carries the type it names, so the client opens a tab that says
    /// the type is unavailable rather than treating the file as plain (diagram-workspace-tabs
    /// Requirements 1.4, 4.4). Everything else is empty: a registration that cannot be served, as
    /// the open refuses it (behaviour change B-27, after B-26), and a file no tool claims or several
    /// editors claim without a default. What the backend refuses to route is not presented as
    /// openable.
    /// </summary>
    private string DiagramMimeTypeOf(string fullPath) =>
        _tools.Route(fullPath) switch
        {
            DiagramRouted diagram => diagram.Definition.Origin.MimeType,
            DesignerRouted designer => designer.Definition.Origin,
            ToolUnreadable => "",
            _ when _diagrams.Route(fullPath) is DiagramUnknownType unknown => unknown.MimeType,
            EditorRouted editor => $"editor/{editor.Definition.Id}",
            _ => "",
        };
}
