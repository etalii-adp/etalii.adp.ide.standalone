using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// What can be done to a selected node, to a toolbox drop and to a finished parent-line gesture,
/// offered as data and answered by the module's commands on the project's history.
/// </summary>
/// <remarks>
/// <para>
/// <b>The ids are the client's</b>, stated once in the client module's <c>abmIds.ts</c>.
/// </para>
/// <para>
/// <b>A drop is placed by where it lands.</b> A behavior tree has no free-standing nodes, so a node
/// dropped from the toolbox goes under the nearest node above the drop that can take another child,
/// among that node's children by how far left it landed. An empty model takes it as its root.
/// </para>
/// <para>
/// <b>Drawing a parent line moves the child.</b> Every node but a root has exactly one parent - its
/// place in the Markdown - so a new line from A to B can only mean "B belongs under A", and is
/// answered by moving B, with everything beneath it, to be A's last child.
/// </para>
/// <para>
/// <b>An added node is renamed in place at once</b>: the add dispatches, and the answer is an input
/// request naming the new node and the rename action, so the inline editor opens over a node that
/// exists (the <c>CommitActionId</c> pattern mindmap uses).
/// </para>
/// </remarks>
public sealed class AbmContextActionProvider : IContextActionProvider
{
    /// <summary>Rename a node in place.</summary>
    public const string RenameActionId = "abm.rename";

    /// <summary>Remove a node and everything beneath it.</summary>
    public const string RemoveActionId = "abm.remove";

    /// <summary>Move a node one place earlier among its siblings - run sooner.</summary>
    public const string MoveEarlierActionId = "abm.move-earlier";

    /// <summary>Move a node one place later among its siblings - run later.</summary>
    public const string MoveLaterActionId = "abm.move-later";

    /// <summary>Edit a node's notes.</summary>
    public const string EditNotesActionId = "abm.edit-notes";

    /// <summary>Draw a parent line: move the child under the parent.</summary>
    public const string ConnectChildActionId = "abm.connect.child";

    /// <summary>Forget every dragged position, so the whole tree is drawn tidy again (<see cref="ArrangeAbmCommand"/>).</summary>
    public const string ArrangeActionId = "abm.arrange";

    private const string AddPrefix = "abm.add.";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IAbmDocumentStore _documents;

    public AbmContextActionProvider(IHistoryStackStore historyStacks, IAbmDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    /// <summary>The add action for one node kind: a child of the target node, or a node at a drop.</summary>
    public static string AddActionId(string kind) => AddPrefix + kind;

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsOurs(target))
        {
            // Another type's element: answered with nothing rather than by reading another notation's file.
            return Result([]);
        }

        // Derived from the DISL definition (AbmDefinition.Menus): its context-menu sets, grouped by
        // group, the ids mapped by its x-abm block. Executing an action by id only finds actions
        // its target discovers, so a drop and a finished gesture each discover what may be run on them.
        return Result(AbmDefinition.Menus(_documents.GetOrLoad(target.ResolvedFullPath), target.ElementId));
    }

    /// <inheritdoc />
    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        var body = target.ResolvedFullPath;
        var model = _documents.GetOrLoad(body).Model;
        var node = model.NodeOf(target.ElementId);

        if (actionId.StartsWith(AddPrefix, StringComparison.Ordinal))
        {
            var kind = actionId[AddPrefix.Length..];
            if (!AbmNodeKinds.IsKnown(kind))
            {
                return new ContextExecutionFailed($"A behavior model has no `{kind}` node.");
            }

            if (node is not null)
            {
                return await AddThenEditAsync(target, new AddAbmNodeCommand(body, kind, node.Id, -1), $"{node.Id}.{node.ChildIds.Count + 1}", kind, cancellationToken);
            }

            if (!GestureIds.TryParsePlacement(target.ElementId, out var x, out var y))
            {
                return new ContextExecutionFailed("A node is added under another node, or by dropping it where it belongs.");
            }

            (AbmNode? parent, int index, string refusal) = PlaceDrop(model, Stored(body), x, y);
            if (refusal.Length > 0)
            {
                return new ContextExecutionFailed(refusal);
            }

            var newId = parent is null ? $"{index + 1}" : $"{parent.Id}.{index + 1}";
            return await AddThenEditAsync(target, new AddAbmNodeCommand(body, kind, parent?.Id ?? "", index), newId, kind, cancellationToken);
        }

        if (actionId == ArrangeActionId)
        {
            return await DispatchAsync(target, new ArrangeAbmCommand(RegistrationOf(body)), cancellationToken);
        }

        if (actionId == ConnectChildActionId)
        {
            if (!GestureIds.TryParseRelation(target.ElementId, out var from, out var to))
            {
                return new ContextExecutionFailed("A parent line is drawn from the parent to the node that belongs under it.");
            }

            return await DispatchAsync(target, new ConnectAbmChildCommand(body, from, to), cancellationToken);
        }

        if (node is null)
        {
            return actionId is RenameActionId or RemoveActionId or MoveEarlierActionId or MoveLaterActionId or EditNotesActionId
                ? new ContextExecutionFailed("That node is no longer in this behavior model.")
                : new ContextExecutionCompleted();
        }

        var place = Siblings(model, node).ToList().FindIndex(sibling => sibling.Id == node.Id);
        switch (actionId)
        {
            case RenameActionId:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Rename", "mdi-pencil-outline", "Label", node.Label, "Rename", node.Id));

            case EditNotesActionId:
                // Not the label, so not marked for the inline editor: notes are the agent's extra
                // instructions for this node, and get the dialog's room.
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Notes", "mdi-note-text-outline", "Notes", node.Notes, "Save"));

            case MoveEarlierActionId:
                return await DispatchAsync(target, new MoveAbmNodeCommand(body, node.Id, node.ParentId ?? "", place - 1), cancellationToken);

            case MoveLaterActionId:
                return await DispatchAsync(target, new MoveAbmNodeCommand(body, node.Id, node.ParentId ?? "", place + 2), cancellationToken);

            case RemoveActionId:
            {
                // The definition's deletion confirmation: how many nodes go with it, asked before it
                // runs; a node with nothing beneath it goes without asking.
                if (AbmDefinition.ElementOf(_documents.GetOrLoad(body).Document.Disl.Diagram, node.Id) is not { } element
                    || DeletionPolicy.Confirmation(AbmDefinition.Specification, element, env: AbmDefinition.Env) is not { } confirmation)
                {
                    return await DispatchAsync(target, new RemoveAbmNodeCommand(body, node.Id), cancellationToken);
                }

                return new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                    confirmation.Title,
                    "mdi-delete-outline",
                    confirmation.Message,
                    confirmation.ConfirmLabel,
                    Danger: confirmation.Danger));
            }

            default:
                return new ContextExecutionCompleted();
        }
    }

    /// <inheritdoc />
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        // A label and notes have no wrong answer; the command refuses what the document cannot hold.
        _ = actionId;
        _ = value;
        return ValueTask.FromResult(ContextValidationResult.Accepted);
    }

    /// <inheritdoc />
    public async ValueTask<ContextCommitResult> CommitAsync(
        ContextTarget target,
        string actionId,
        string value,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        _ = text;

        var body = target.ResolvedFullPath;
        var id = target.ElementId;
        var exists = _documents.GetOrLoad(body).Model.NodeOf(id) is not null;

        ICommand? command = actionId switch
        {
            RenameActionId when exists => new RenameAbmNodeCommand(body, id, value),
            EditNotesActionId when exists => new SetAbmNotesCommand(body, id, value),
            RemoveActionId when exists => new RemoveAbmNodeCommand(body, id),
            _ => null,
        };

        if (command is null)
        {
            return ContextCommitResult.Failed($"'{actionId}' does not apply to this selection.");
        }

        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

    /// <summary>
    /// Where a node dropped at (<paramref name="x"/>, <paramref name="y"/>) belongs: under the nearest
    /// node above it that takes another child, before the first of its children drawn right of it.
    /// </summary>
    internal static (AbmNode? Parent, int Index, string Refusal) PlaceDrop(
        AbmModel model, IReadOnlyDictionary<string, RegistrationPosition> stored, double x, double y)
    {
        if (model.Nodes.Count == 0)
        {
            return (null, 0, "");
        }

        var positions = AbmLayout.Arrange(model, stored);
        static (double X, double Y) Centre(RegistrationPosition topLeft) =>
            (topLeft.X + (AbmLayout.NodeWidth / 2), topLeft.Y + (AbmLayout.NodeHeight / 2));

        var parent = model.Nodes
            .Where(node => node.TakesAnotherChild && Centre(positions[node.Id]).Y < y)
            .OrderBy(node =>
            {
                (double cx, double cy) = Centre(positions[node.Id]);
                return Math.Abs(x - cx) + (2 * (y - cy));
            })
            .FirstOrDefault();

        if (parent is null)
        {
            return (null, 0, "Drop the node below the node it belongs under: a Do in order, a Try in order, a Do together, or a Retry, Repeat until, Only while or Ask approval before that has no child yet.");
        }

        var index = model.ChildrenOf(parent).Count(child => Centre(positions[child.Id]).X < x);
        return (parent, index, "");
    }

    private async ValueTask<ContextExecutionResult> AddThenEditAsync(
        ContextTarget target, AddAbmNodeCommand command, string newId, string kind, CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return new ContextExecutionFailed(result.Error);
        }

        return new ContextExecutionRequiresInput(new ContextInputRequest(
            "Rename", "mdi-pencil-outline", "Label", AddAbmNodeCommandHandler.StartingLabel(kind), "Rename", newId, RenameActionId));
    }

    private async ValueTask<ContextExecutionResult> DispatchAsync(ContextTarget target, ICommand command, CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    /// <summary>The positions an author dragged nodes to, from the registration beside the Markdown.</summary>
    private static IReadOnlyDictionary<string, RegistrationPosition> Stored(string bodyPath) =>
        RegistrationLayout.Read(RegistrationOf(bodyPath));

    /// <summary>The registration beside the Markdown, where dragged positions are kept.</summary>
    private static string RegistrationOf(string bodyPath) =>
        System.IO.Path.ChangeExtension(bodyPath, DiagramFileName.Extension);

    private static IReadOnlyList<AbmNode> Siblings(AbmModel model, AbmNode node) =>
        node.ParentId is { } parentId ? model.ChildrenOf(model.NodeOf(parentId)!) : model.Roots;

    private static bool IsOurs(ContextTarget target) =>
        target.Origin == Diagram.AgentBehaviorModelling.Origin;

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Result(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        ValueTask.FromResult(groups);
}
