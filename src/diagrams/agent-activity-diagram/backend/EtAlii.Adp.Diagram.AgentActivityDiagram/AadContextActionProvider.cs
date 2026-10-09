using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>
/// What can be done to an activity file from the canvas: the menu the definition derives for an
/// element, a row or a relation, and the gestures the canvas makes on its own - a drop, a drawn
/// line, a fold, the archived switch.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three entries of the derived menu are answered differently from how the definition words
/// them</b>, each because of where this host can do the work. <i>Lock position</i> is left out: an
/// element is locked by putting it somewhere, and the backend does not know where the layout drew
/// an element nobody has moved. <i>Unlock position</i> is offered on a locked element only. And the
/// three <i>Open link</i> operations are left out: a link is opened by its symbol on the canvas,
/// by the one rule the client keeps for what may be opened.
/// </para>
/// <para>
/// <b>Everything goes through the project's history</b>, so each is one undo away, and nothing
/// writes the file except a command.
/// </para>
/// </remarks>
public sealed class AadContextActionProvider : IContextActionProvider
{
    /// <summary>What precedes a toolbox tool's id in the action a drop of it executes.</summary>
    public const string AddActionPrefix = "add-";

    private const string ConnectActionId = "connect";

    private const string RenameActionId = "editLabel";

    private const string AddRowActionId = "create-child";

    private const string RemoveActionId = "delete";

    private const string UnpinActionId = "unpin";

    private const string UnpinAllActionId = "unpinAll";

    /// <summary>What precedes a task status in the action that sets it.</summary>
    private const string SetTaskStatusPrefix = "setTaskStatus:";

    private const string CollapseGroupActionId = "collapse-group";

    private const string ExpandGroupActionId = "expand-group";

    public const string ShowArchivedActionId = "show-archived";

    private const string HideArchivedActionId = "hide-archived";

    private const string SetTaskStatusOperation = "setTaskStatus";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IAadDocumentStore _documents;

    public AadContextActionProvider(IHistoryStackStore historyStacks, IAadDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (target.Origin != Diagram.AgentActivity.Origin)
        {
            return Result([]);
        }

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);
        if (!entry.IsUsable)
        {
            return Result([]);
        }

        // Executing an action by id only finds actions its target discovers, so each gesture's
        // target discovers what the gesture executes against it.
        var id = target.ElementId;
        if (GestureIds.IsPlacement(id))
        {
            return Result(
            [
                new ContextActionGroupDefinition([.. AadDefinition.Toolbox.Select(tool => new ContextActionDefinition(tool.DropActionId, $"Add {tool.Label.ToLowerInvariant()}", tool.Icon))]),
                new ContextActionGroupDefinition([new ContextActionDefinition(
                    UnpinAllActionId, "Unlock all positions", "mdi-lock-open-variant-outline", Available: entry.Model.Placements.Count > 0, UnavailableReason: "No position is locked.")]),
            ]);
        }

        if (GestureIds.IsRelation(id))
        {
            return Result([new ContextActionGroupDefinition([new ContextActionDefinition(ConnectActionId, "Connect", "mdi-vector-line")])]);
        }

        if (id == AadElementMapper.ViewId)
        {
            return Result([new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(ShowArchivedActionId, "Show archived", "mdi-archive-eye-outline"),
                new ContextActionDefinition(HideArchivedActionId, "Hide archived", "mdi-archive-off-outline"),
            ])]);
        }

        if (AadGroupTarget.TryParse(id, out _, out _))
        {
            return Result([new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(CollapseGroupActionId, "Fold", "mdi-chevron-right"),
                new ContextActionDefinition(ExpandGroupActionId, "Unfold", "mdi-chevron-down"),
            ])]);
        }

        if (AadDefinition.ElementOf(entry.Document.Disl.Diagram, id) is not { } element)
        {
            return Result([]);
        }

        var pinned = entry.Model.Placements.ContainsKey(id);
        List<ContextActionGroupDefinition> groups = [];
        foreach (var group in AadDefinition.Menus(element))
        {
            var actions = group.Entries.SelectMany(derived => Action(derived, pinned)).ToList();
            if (actions.Count > 0)
            {
                groups.Add(new ContextActionGroupDefinition(actions));
            }
        }

        return Result(groups);
    }

    /// <inheritdoc />
    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(actionId);
        cancellationToken.ThrowIfCancellationRequested();

        var body = target.ResolvedFullPath;
        var entry = _documents.GetOrLoad(body);
        if (!entry.IsUsable)
        {
            return new ContextExecutionFailed($"{System.IO.Path.GetFileName(body)} could not be read, so it cannot be edited.");
        }

        var id = target.ElementId;
        var diagram = entry.Document.Disl.Diagram;
        var element = AadDefinition.ElementOf(diagram, id);

        return actionId switch
        {
            // The drop said everything an add needs, so nothing is asked.
            var add when add.StartsWith(AddActionPrefix, StringComparison.Ordinal) => GestureIds.TryParsePlacement(id, out var x, out var y)
                ? await DispatchAsync(target, new AddAadElementCommand(body, add[AddActionPrefix.Length..], x, y), cancellationToken)
                : new ContextExecutionFailed("An element is added by dropping it where it should be."),

            ConnectActionId => GestureIds.TryParseRelation(id, out var from, out var to)
                ? await DispatchAsync(target, new ConnectAadCommand(body, from, to), cancellationToken)
                : new ContextExecutionFailed("A relation is drawn from one element to another."),

            UnpinAllActionId => await DispatchAsync(target, new UnpinAadElementCommand(body), cancellationToken),

            ShowArchivedActionId or HideArchivedActionId => await DispatchAsync(target, new SetAadShowArchivedCommand(body, actionId == ShowArchivedActionId), cancellationToken),

            CollapseGroupActionId or ExpandGroupActionId => AadGroupTarget.TryParse(id, out var owner, out var group)
                ? await DispatchAsync(target, new SetAadGroupCommand(body, owner, group, actionId == CollapseGroupActionId), cancellationToken)
                : new ContextExecutionFailed("A group is folded on the element that lists it."),

            // An element is renamed in place, over its own label; a row has no label to edit in
            // place, so its title is asked for.
            RenameActionId when element is { Type.IsRelation: false } => new ContextExecutionRequiresInput(new ContextInputRequest(
                "Rename", "mdi-rename-outline", "Name", AadDefinition.NameOf(element), "Rename", element.Parent is null ? id : "")),

            AddRowActionId when element is not null => await DispatchAsync(target, new AddAadRowCommand(body, id), cancellationToken),

            UnpinActionId when element is not null => await DispatchAsync(target, new UnpinAadElementCommand(body, id), cancellationToken),

            var status when status.StartsWith(SetTaskStatusPrefix, StringComparison.Ordinal) && element is not null => await DispatchAsync(target, new SetAadTaskStatusCommand(body, id, status[SetTaskStatusPrefix.Length..]), cancellationToken),

            RemoveActionId when element is { Type.IsRelation: true } => await DispatchAsync(target, new DisconnectAadCommand(body, id), cancellationToken),

            // The definition's confirmation, asked before anything goes; with none, no ceremony.
            RemoveActionId when element is not null => DeletionPolicy.Confirmation(AadDefinition.Specification, element, env: AadDefinition.Env()) is { } confirmation
                ? new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                    confirmation.Title, "mdi-delete-outline", confirmation.Message, confirmation.ConfirmLabel, Danger: confirmation.Danger))
                : await DispatchAsync(target, new RemoveAadElementCommand(body, id), cancellationToken),

            RenameActionId or AddRowActionId or UnpinActionId or RemoveActionId => new ContextExecutionFailed("That is no longer in this diagram."),

            _ => new ContextExecutionCompleted(),
        };
    }

    /// <inheritdoc />
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        // The one value asked for is a name, and the only wrong one is an empty one.
        return ValueTask.FromResult(actionId == RenameActionId && string.IsNullOrWhiteSpace(value)
            ? ContextValidationResult.Rejected("It needs a name.")
            : ContextValidationResult.Accepted);
    }

    /// <inheritdoc />
    public async ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        _ = text;

        var body = target.ResolvedFullPath;
        var id = target.ElementId;
        var entry = _documents.GetOrLoad(body);
        var element = entry.IsUsable ? AadDefinition.ElementOf(entry.Document.Disl.Diagram, id) : null;

        ICommand? command = actionId switch
        {
            RenameActionId when element is { Type.IsRelation: false } && AadDefinition.LabelAttribute(element) is { } label => new SetAadAttributeCommand(body, id, label, value),
            RemoveActionId when element is { Type.IsRelation: false } => new RemoveAadElementCommand(body, id),
            _ => null,
        };
        if (command is null)
        {
            return ContextCommitResult.Failed($"'{actionId}' does not apply to this selection.");
        }

        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

    /// <summary>A derived menu entry as this host offers it: none, itself, or a submenu of the values it can set.</summary>
    private static IEnumerable<ContextActionDefinition> Action(DerivedMenuEntry entry, bool pinned)
    {
        ContextShortcutDefinition? shortcut = entry.Shortcut is { } key ? new ContextShortcutDefinition(key.Key, key.Ctrl, key.Shift, key.Alt, key.Meta) : null;
        switch (entry)
        {
            case { Kind: "pin" } or { Kind: "operation", Operation: "openLink" or "openBranchLink" or "openFolderLink" }:
                yield break;

            case { Kind: "unpin" }:
                yield return new ContextActionDefinition(entry.Id, entry.Label, entry.Icon, shortcut, pinned, pinned ? "" : "Its position is not locked: the layout places it.");
                yield break;

            case { Operation: SetTaskStatusOperation }:
                // The operation's one parameter, asked as a submenu: one entry per status.
                yield return new ContextActionDefinition(entry.Id, entry.Label, entry.Icon, shortcut, entry.Available, entry.UnavailableReason,
                [
                    new ContextActionGroupDefinition([.. AadDefinition.TaskStatus.Members.Select(status =>
                        new ContextActionDefinition(SetTaskStatusPrefix + status.Name, status.Label, "mdi-circle-small"))]),
                ]);
                yield break;

            default:
                yield return new ContextActionDefinition(entry.Id, entry.Label, entry.Icon, shortcut, entry.Available, entry.UnavailableReason);
                yield break;
        }
    }

    private async ValueTask<ContextExecutionResult> DispatchAsync(ContextTarget target, ICommand command, CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Result(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        ValueTask.FromResult(groups);
}

/// <summary>
/// The target of a fold: one group of one element, written <c>element#group</c>. An id in the file
/// cannot hold a <c>#</c> the binding would read, so the last one splits the two.
/// </summary>
public static class AadGroupTarget
{
    public static string Of(string elementId, string group) => $"{elementId}#{group}";

    public static bool TryParse(string? target, out string elementId, out string group)
    {
        elementId = "";
        group = "";
        var cut = target?.LastIndexOf('#') ?? -1;
        if (target is null || cut <= 0 || cut == target.Length - 1)
        {
            return false;
        }

        elementId = target[..cut];
        group = target[(cut + 1)..];
        return true;
    }
}
