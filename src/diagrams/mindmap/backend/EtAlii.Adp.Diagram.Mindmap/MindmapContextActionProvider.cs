using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using Serilog;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// What a user can do to a mindmap node, offered the way every other action is - so it
/// reaches the ribbon, the menu and the keyboard through one path (Requirement 8.4). Each
/// action that changes the map builds a command and dispatches it through the history; the
/// provider itself never touches the document (Requirement 6.1). Folding is the exception
/// that proves the rule: it changes the viewer's state, not the map, so it is no command.
/// </summary>
/// <remarks>
/// The shortcuts are Freeplane's (Requirement 8.1). Three of them - F2, Delete, Insert - are
/// also the explorer's, and the two coexist because a shortcut is resolved against the
/// innermost selection: a node-innermost selection reaches here, an entry-innermost one
/// reaches the explorer's providers (Requirement 8.7). Tab, the XMind convention for a
/// child, is an alias the canvas maps to Insert, since an action carries one shortcut.
/// </remarks>
public sealed class MindmapContextActionProvider : IContextActionProvider
{
    public const string AddChildActionId = "mindmap.add-child";
    public const string AddSiblingActionId = "mindmap.add-sibling";
    public const string RenameActionId = "mindmap.rename";
    public const string DeleteActionId = "mindmap.delete";
    public const string ToggleFoldActionId = "mindmap.toggle-fold";
    public const string EditNotesActionId = "mindmap.edit-notes";
    public const string LinkActionId = "mindmap.link";
    public const string UnlinkActionId = "mindmap.unlink";

    private const string NodeGone = "The node no longer exists.";

    private static readonly ILogger Logger = Log.ForContext<MindmapContextActionProvider>();

    private static readonly string[] SkippedFolders = [".git", "node_modules", "bin", "obj", ".claude"];

    private readonly IHistoryStackStore _historyStacks;
    private readonly IMindmapDocumentStore _documents;
    private readonly MindmapViewState _views;

    public MindmapContextActionProvider(IHistoryStackStore historyStacks, IMindmapDocumentStore documents, MindmapViewState views)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(views);
        _historyStacks = historyStacks;
        _documents = documents;
        _views = views;
    }

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        var node = Resolve(target, out var view);
        if (node is null)
        {
            return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([]);
        }

        // Not offered rather than greyed (Requirement 8.6): a sibling of the root and an
        // unlink of an unlinked node are not "unavailable right now", they do not apply.
        var structure = new List<ContextActionDefinition>
        {
            new(AddChildActionId, "Add child", "mdi-subdirectory-arrow-right", new ContextShortcutDefinition("Insert")),
        };
        if (!node.IsRoot)
        {
            structure.Add(new ContextActionDefinition(AddSiblingActionId, "Add sibling", "mdi-plus", new ContextShortcutDefinition("Enter")));
        }

        structure.Add(new ContextActionDefinition(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")));
        if (!node.IsRoot)
        {
            structure.Add(new ContextActionDefinition(DeleteActionId, "Delete", "mdi-trash-can-outline", new ContextShortcutDefinition("Delete")));
        }

        var content = new List<ContextActionDefinition>
        {
            new(EditNotesActionId, node.Notes.Length > 0 ? "Edit notes…" : "Add notes…", "mdi-note-text-outline"),
            new(LinkActionId, node.Link is null ? "Link to…" : "Change link…", "mdi-link-variant"),
        };
        if (node.Link is not null)
        {
            content.Add(new ContextActionDefinition(UnlinkActionId, "Unlink", "mdi-link-variant-off"));
        }

        var groups = new List<ContextActionGroupDefinition> { new(structure), new(content) };
        if (node.HasChildren)
        {
            var folded = view!.IsFolded(node);
            groups.Add(new ContextActionGroupDefinition([
                new ContextActionDefinition(ToggleFoldActionId, folded ? "Expand" : "Collapse", folded ? "mdi-unfold-more-horizontal" : "mdi-unfold-less-horizontal", new ContextShortcutDefinition(" ")),
            ]));
        }

        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>(groups);
    }

    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        var node = Resolve(target, out _);
        if (node is null)
        {
            return Result(new ContextExecutionFailed(NodeGone));
        }

        switch (actionId)
        {
            case AddChildActionId:
                return Result(new ContextExecutionRequiresInput(
                    new ContextInputRequest("Add child", "mdi-subdirectory-arrow-right", "Text", "", "Add")));

            case AddSiblingActionId when !node.IsRoot:
                return Result(new ContextExecutionRequiresInput(
                    new ContextInputRequest("Add sibling", "mdi-plus", "Text", "", "Add")));

            case RenameActionId:
                // The one prompt here whose value IS the text on screen, so it carries the
                // element id and the canvas may render it in place of the node's label. The
                // three input prompts around it deliberately do not: a child's text does not
                // exist yet, a sibling's neither, and notes are not the label
                // (inline-rename Requirement 3.1).
                return Result(new ContextExecutionRequiresInput(
                    new ContextInputRequest("Rename node", "mdi-pencil-outline", "Text", node.Text, "Rename", node.Id)));

            case DeleteActionId when !node.IsRoot:
                // A leaf goes without asking; a branch is confirmed, since it takes its
                // subtree with it (Requirement 7.4). Both are undoable, and the message says so.
                if (!node.HasChildren)
                {
                    return DispatchAsync(target, new RemoveNodeCommand(target.ResolvedFullPath, node.Id), cancellationToken);
                }

                return Result(new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                    "Delete branch?",
                    "mdi-trash-can-outline",
                    $"Delete '{node.Text}' and the {CountDescendants(node)} nodes under it? You can undo this.",
                    "Delete",
                    Danger: true)));

            case ToggleFoldActionId when node.HasChildren:
                // View state, not a command: nothing is written and nothing lands on the history
                // (Requirements 9.4, 9.6). Toggled through the view state's announcing method,
                // which is what makes the session push the group/ungroup delta - toggling the
                // view directly would change state no client ever hears of.
                _views.Toggle(target.WatchId, target.ResolvedFullPath, _documents.GetOrLoad(target.ResolvedFullPath), node.Id);
                return Result(new ContextExecutionCompleted());

            case EditNotesActionId:
                return Result(new ContextExecutionRequiresInput(
                    new ContextInputRequest("Notes", "mdi-note-text-outline", "Notes", node.Notes, "Save")));

            case LinkActionId:
                return Result(new ContextExecutionRequiresChoice(new ContextChoiceRequest(
                    "Link to",
                    "mdi-link-variant",
                    "Link",
                    ProjectTree(target.RootPath),
                    "The project has no files to link to.")));

            case UnlinkActionId when node.Link is not null:
                return DispatchAsync(target, new SetNodeLinkCommand(target.ResolvedFullPath, node.Id, null), cancellationToken);

            default:
                return Result(new ContextExecutionFailed($"Unknown action '{actionId}'."));
        }
    }

    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
        // Any text is a valid node text, the empty string included (Requirement 7.6), and a
        // link target is chosen rather than typed, so there is nothing to judge here.
        ValueTask.FromResult(ContextValidationResult.Accepted);

    public async ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
    {
        var node = Resolve(target, out _);
        if (node is null)
        {
            return ContextCommitResult.Failed(NodeGone);
        }

        var bodyPath = target.ResolvedFullPath;
        ICommand? command = actionId switch
        {
            AddChildActionId => new AddChildNodeCommand(bodyPath, node.Id, MindmapDocument.NewId(), value),
            AddSiblingActionId when !node.IsRoot => new AddSiblingNodeCommand(bodyPath, node.Id, MindmapDocument.NewId(), value),
            RenameActionId => new SetNodeTextCommand(bodyPath, node.Id, value),
            DeleteActionId when !node.IsRoot => new RemoveNodeCommand(bodyPath, node.Id),
            EditNotesActionId => new SetNodeNotesCommand(bodyPath, node.Id, value),
            LinkActionId => LinkCommand(target, node, value),
            _ => null,
        };

        if (command is null)
        {
            return ContextCommitResult.Failed($"Unknown action '{actionId}'.");
        }

        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

    private static SetNodeLinkCommand LinkCommand(ContextTarget target, MindmapNode node, string projectRelative)
    {
        var segments = projectRelative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stored = MindmapLinks.ToMapRelative(target.ResolvedFullPath, segments, target.RootPath);
        return new SetNodeLinkCommand(target.ResolvedFullPath, node.Id, stored);
    }

    private async ValueTask<ContextExecutionResult> DispatchAsync(ContextTarget target, ICommand command, CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    private MindmapNode? Resolve(ContextTarget target, out MindmapConnectionView? view)
    {
        view = null;
        if (target.Scope != ContextScope.DiagramElement || target.ElementId.Length == 0)
        {
            return null;
        }

        // A provider is consulted for every diagram element in its scope, including other
        // types'. Answering for a file this module does not own would mean parsing another
        // notation's document - which is exactly what happened: every action lookup on a .tml
        // logged a warning about it not being well-formed XML.
        if (!target.ResolvedFullPath.EndsWith(Diagram.DocumentExtension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        MindmapDocument document;
        try
        {
            document = _documents.GetOrLoad(target.ResolvedFullPath);
        }
        catch (MindmapFormatException exception)
        {
            Logger.Warning(exception, "Cannot offer actions on {BodyPath}", target.ResolvedFullPath);
            return null;
        }

        var node = document.Find(target.ElementId);
        if (node is not null)
        {
            view = _views.For(target.WatchId, target.ResolvedFullPath, document);
        }

        return node;
    }

    private static int CountDescendants(MindmapNode node) =>
        node.Children.Sum(child => 1 + CountDescendants(child));

    private static ValueTask<ContextExecutionResult> Result(ContextExecutionResult result) => ValueTask.FromResult(result);

    /// <summary>
    /// The project's files as a choice tree, folders unselectable and files selectable, each
    /// file's id its project-relative path - so a link target is picked, never typed
    /// (Requirement 12.7).
    /// </summary>
    private static IReadOnlyList<ContextOptionNode> ProjectTree(string rootPath)
    {
        if (rootPath.Length == 0 || !Directory.Exists(rootPath))
        {
            return [];
        }

        return Options(rootPath, rootPath);
    }

    private static IReadOnlyList<ContextOptionNode> Options(string folder, string rootPath)
    {
        var options = new List<ContextOptionNode>();
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return options;
        }

        foreach (var entry in entries)
        {
            var name = IoPath.GetFileName(entry);
            var id = string.Join('/', MindmapLinks.ProjectRelative(entry, rootPath));
            if (Directory.Exists(entry))
            {
                if (SkippedFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var children = Options(entry, rootPath);
                if (children.Count > 0)
                {
                    options.Add(new ContextOptionNode(id, name, Selectable: true, children));
                }
            }
            else if (!name.StartsWith("~adp-", StringComparison.Ordinal))
            {
                options.Add(new ContextOptionNode(id, name, Selectable: true));
            }
        }

        return options;
    }
}
