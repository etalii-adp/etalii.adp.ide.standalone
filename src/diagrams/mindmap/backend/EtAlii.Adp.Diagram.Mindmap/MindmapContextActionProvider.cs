using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
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
/// <para>
/// <b>What is offered is derived from the DISL definition</b> (<see cref="MindmapDefinition.Menus"/>):
/// its context menu, which entries apply to the node, their labels, icons, shortcuts and groups, and
/// the ids of its <c>x-mindmap</c> block. An action the node's menu does not offer is not run against
/// it, here or on commit: it is refused as unknown, as it always was. What each action does and what
/// it asks stays here; whether a delete asks first, and in which words, is the definition's
/// <c>deletion.confirm</c>.
/// </para>
/// <para>
/// The shortcuts are Freeplane's (Requirement 8.1). Three of them - F2, Delete, Insert - are
/// also the explorer's, and the two coexist because a shortcut is resolved against the
/// innermost selection: a node-innermost selection reaches here, an entry-innermost one
/// reaches the explorer's providers (Requirement 8.7). Tab, the XMind convention for a
/// child, is an alias the canvas maps to Insert, since an action carries one shortcut.
/// </para>
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
        // Not offered rather than greyed (Requirement 8.6): a sibling of the root and an unlink of
        // an unlinked node are not "unavailable right now", they do not apply - the definition's
        // entries say so with `visible`.
        return ValueTask.FromResult(Resolve(target, out var element) is null ? [] : MindmapDefinition.Menus(element!));
    }

    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        var node = Resolve(target, out var element);
        if (node is null)
        {
            return Result(new ContextExecutionFailed(NodeGone));
        }

        if (!MindmapDefinition.Offers(element!, actionId))
        {
            return Result(new ContextExecutionFailed($"Unknown action '{actionId}'."));
        }

        switch (actionId)
        {
            // ADD, THEN EDIT IN PLACE. Both of these used to open a dialog asking for a name
            // before the node existed, which asks the user to name a thing they cannot see.
            // The node is created at once under a name read off the siblings it is joining,
            // and the prompt that follows is an inline editor over the node itself - the same
            // gesture as any other rename, on something already on screen.
            //
            // The rename action id is what the value commits under; without it the commit runs
            // THIS action again and adds a second node. The service re-points the interaction
            // at the new node, so the rename lands on it rather than on its parent.
            case AddChildActionId:
                return AddThenEditAsync(target, node.Children, isChild: true, cancellationToken);

            case AddSiblingActionId:
                return AddThenEditAsync(target, node.Parent?.Children ?? [], isChild: false, cancellationToken);

            case RenameActionId:
                // The one prompt here whose value IS the text on screen, so it carries the
                // element id and the canvas may render it in place of the node's label. The
                // three input prompts around it deliberately do not: a child's text does not
                // exist yet, a sibling's neither, and notes are not the label
                // (inline-rename Requirement 3.1).
                return Result(new ContextExecutionRequiresInput(
                    new ContextInputRequest("Rename node", "mdi-pencil-outline", "Text", node.Text, "Rename", node.Id)));

            case DeleteActionId:
                // A leaf goes without asking; a branch is confirmed, since it takes its
                // subtree with it (Requirement 7.4). Both are undoable, and the message says so.
                // Which, and the texts, are the definition's `deletion.confirm`.
                return MindmapDefinition.Confirmation(element!) is { } confirmation
                    ? Result(new ContextExecutionRequiresConfirmation(confirmation))
                    : DispatchAsync(target, new RemoveNodeCommand(target.ResolvedFullPath, node.Id), cancellationToken);

            case ToggleFoldActionId:
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

            case UnlinkActionId:
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
        var node = Resolve(target, out var element);
        if (node is null)
        {
            return ContextCommitResult.Failed(NodeGone);
        }

        var bodyPath = target.ResolvedFullPath;
        ICommand? command = !MindmapDefinition.Offers(element!, actionId)
            ? null
            : actionId switch
            {
                AddChildActionId => new AddChildNodeCommand(bodyPath, node.Id, MindmapDocument.NewId(), value),
                AddSiblingActionId => new AddSiblingNodeCommand(bodyPath, node.Id, MindmapDocument.NewId(), value),
                RenameActionId => new SetNodeTextCommand(bodyPath, node.Id, value),
                DeleteActionId => new RemoveNodeCommand(bodyPath, node.Id),
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

    /// <summary>
    /// Adds a node named from the siblings it joins, then asks for its label in place.
    /// </summary>
    /// <remarks>
    /// The name is a starting point rather than a decision: the inline editor opens on it
    /// immediately, so the usual path replaces it before anybody reads it. It still has to be a
    /// GOOD starting point, because cancelling the rename leaves it - which is why it follows
    /// the siblings' own pattern rather than being a fixed placeholder.
    /// </remarks>
    private async ValueTask<ContextExecutionResult> AddThenEditAsync(
        ContextTarget target,
        IReadOnlyList<MindmapNode> siblings,
        bool isChild,
        CancellationToken cancellationToken)
    {
        var node = Resolve(target, out _);
        if (node is null)
        {
            return new ContextExecutionFailed(NodeGone);
        }

        var newId = MindmapDocument.NewId();
        var name = SiblingNaming.NextName(siblings.Select(sibling => sibling.Text));
        var body = target.ResolvedFullPath;
        ICommand command = isChild
            ? new AddChildNodeCommand(body, node.Id, newId, name)
            : new AddSiblingNodeCommand(body, node.Id, newId, name);

        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return new ContextExecutionFailed(result.Error);
        }

        return new ContextExecutionRequiresInput(new ContextInputRequest(
            "Rename node", "mdi-pencil-outline", "Text", name, "Rename", newId, RenameActionId));
    }

    private async ValueTask<ContextExecutionResult> DispatchAsync(ContextTarget target, ICommand command, CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    /// <summary>The node the target names, and its element in the DISL model as the asking viewer sees it.</summary>
    private MindmapNode? Resolve(ContextTarget target, out DislElement? element)
    {
        element = null;
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
            var model = MindmapDefinition.ModelOf(document, _views.For(target.WatchId, target.ResolvedFullPath, document));
            element = MindmapDefinition.ElementOf(model, node.Id);
        }

        return element is null ? null : node;
    }

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
