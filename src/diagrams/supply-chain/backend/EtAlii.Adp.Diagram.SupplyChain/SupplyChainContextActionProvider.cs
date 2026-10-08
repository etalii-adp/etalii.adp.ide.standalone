using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// What can be done to a selected group, node or flow, to empty canvas, and to a finished connect
/// gesture - offered as data, each answered by a command on the project's history.
/// </summary>
/// <remarks>
/// <para>
/// <b>The ids are the client's</b>, stated once in the client module's <c>supplyChainIds.ts</c>; this
/// provider answers exactly those strings.
/// </para>
/// <para>
/// <b>Every action that needs no input dispatches here, in <see cref="ExecuteAsync"/></b>: a
/// Completed execution never reaches the commit leg, so answering Completed without dispatching
/// would do nothing at all.
/// </para>
/// </remarks>
public sealed class SupplyChainContextActionProvider : IContextActionProvider
{
    /// <summary>Rename a node or a group in place, or a flow's product.</summary>
    private const string RenameActionId = "supply-chain.rename";

    /// <summary>Add one step to a node's quantity or a flow's volume.</summary>
    public const string IncreaseActionId = "supply-chain.increase";

    /// <summary>Take one step from a node's quantity or a flow's volume.</summary>
    public const string DecreaseActionId = "supply-chain.decrease";

    /// <summary>Put a node in a new group, asking for its name.</summary>
    public const string GroupActionId = "supply-chain.group";

    /// <summary>Remove a node with its flows, a flow, or a group - its members stay.</summary>
    public const string RemoveActionId = "supply-chain.remove";

    /// <summary>Lay the whole diagram out left to right.</summary>
    public const string ArrangeActionId = "supply-chain.arrange";

    /// <summary>Draw a flow for a finished connect gesture.</summary>
    public const string ConnectActionId = "supply-chain.connect";

    /// <summary>Add an empty group where it was dropped.</summary>
    public const string AddGroupActionId = "supply-chain.add-group";

    private const string AddPrefix = "supply-chain.add.";

    private readonly IHistoryStackStore _historyStacks;
    private readonly ISupplyChainDocumentStore _documents;

    public SupplyChainContextActionProvider(IHistoryStackStore historyStacks, ISupplyChainDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    /// <summary>The add action for one stage.</summary>
    public static string AddActionId(string stage) => AddPrefix + stage;

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Diagram.IsBody(target.ResolvedFullPath))
        {
            return Result([]);
        }

        var model = _documents.GetOrLoad(target.ResolvedFullPath).Model;
        var arrange = new ContextActionDefinition(
            ArrangeActionId, "Arrange diagram", "mdi-sitemap-outline", null,
            model.Nodes.Count > 0, "There is nothing to arrange until this diagram has a node.");

        if (SupplyChainEdits.NodeOf(model, target.ElementId) is { } node)
        {
            var atZero = (node.Quantity ?? 0) <= 0;
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(IncreaseActionId, "Increase quantity", "mdi-plus-circle-outline", new ContextShortcutDefinition("+")),
                    new ContextActionDefinition(DecreaseActionId, "Decrease quantity", "mdi-minus-circle-outline", new ContextShortcutDefinition("-"), !atZero, "The quantity is already zero."),
                ]),
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(GroupActionId, "Put in a new group…", "mdi-group"),
                    new ContextActionDefinition(RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
                ]),
                new ContextActionGroupDefinition([arrange]),
            ]);
        }

        if (SupplyChainEdits.FlowOf(model, target.ElementId) is { } flow)
        {
            var atZero = (flow.Volume ?? 0) <= 0;
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(IncreaseActionId, "Increase volume", "mdi-plus-circle-outline", new ContextShortcutDefinition("+")),
                    new ContextActionDefinition(DecreaseActionId, "Decrease volume", "mdi-minus-circle-outline", new ContextShortcutDefinition("-"), !atZero, "The volume is already zero."),
                ]),
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameActionId, "Rename product…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(RemoveActionId, "Remove flow", "mdi-vector-polyline-remove", new ContextShortcutDefinition("Delete")),
                ]),
            ]);
        }

        if (SupplyChainEdits.GroupOf(model, target.ElementId) is not null)
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(RemoveActionId, "Remove group", "mdi-ungroup", new ContextShortcutDefinition("Delete")),
                ]),
                new ContextActionGroupDefinition([arrange]),
            ]);
        }

        // A drop, a background menu and a finished gesture each discover what may be executed
        // against them, because executing an action by id only finds actions its target discovers.
        if (GestureIds.TryParsePlacement(target.ElementId, out _, out _))
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                    [.. SupplyChainNodeTypes.All.Select(stage => new ContextActionDefinition(AddActionId(stage), $"Add {SupplyChainNodeTypes.Display(stage).ToLowerInvariant()} here", SupplyChainStageIcons.Of(stage)))]),
                new ContextActionGroupDefinition([new ContextActionDefinition(AddGroupActionId, "Add group here", SupplyChainStageIcons.Group)]),
                new ContextActionGroupDefinition([arrange]),
            ]);
        }

        if (GestureIds.TryParseRelation(target.ElementId, out _, out _))
        {
            return Result([new ContextActionGroupDefinition([new ContextActionDefinition(ConnectActionId, "Add flow", "mdi-arrow-right-thin")])]);
        }

        return Result([]);
    }

    /// <inheritdoc />
    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        var body = target.ResolvedFullPath;
        var id = target.ElementId;
        var model = _documents.GetOrLoad(body).Model;
        var node = SupplyChainEdits.NodeOf(model, id);
        var flow = SupplyChainEdits.FlowOf(model, id);
        var group = SupplyChainEdits.GroupOf(model, id);

        if (actionId.StartsWith(AddPrefix, StringComparison.Ordinal))
        {
            var stage = actionId[AddPrefix.Length..];
            return SupplyChainNodeTypes.IsKnown(stage) && GestureIds.TryParsePlacement(id, out var x, out var y)
                ? await DispatchAsync(target, new AddSupplyChainNodeCommand(body, stage, x, y), cancellationToken)
                : new ContextExecutionFailed("A node is added by dropping it where it belongs.");
        }

        switch (actionId)
        {
            case AddGroupActionId:
                return GestureIds.TryParsePlacement(id, out var groupX, out var groupY)
                    ? await DispatchAsync(target, new AddSupplyChainGroupCommand(body, groupX, groupY), cancellationToken)
                    : new ContextExecutionFailed("A group is added by dropping it where it belongs.");

            case ConnectActionId:
                return GestureIds.TryParseRelation(id, out var from, out var to)
                    ? await DispatchAsync(target, new ConnectSupplyChainNodesCommand(body, from, to), cancellationToken)
                    : new ContextExecutionFailed("A flow is drawn from one node to another.");

            case ArrangeActionId:
                return await DispatchAsync(target, new ArrangeSupplyChainCommand(body), cancellationToken);

            case IncreaseActionId or DecreaseActionId when node is not null || flow is not null:
                return await DispatchAsync(target, new StepSupplyChainValueCommand(body, id, actionId == IncreaseActionId ? 1 : -1), cancellationToken);

            case RenameActionId when node is not null:
                return Input("Rename", "Name", node.Name, "Rename", id);

            case RenameActionId when group is not null:
                return Input("Rename", "Name", group.Name, "Rename", id);

            case RenameActionId when flow is not null:
                return Input("Rename product", "Product", flow.Product, "Rename", id);

            case GroupActionId when node is not null:
                // Not in place: the value is a new group's name, which is no label on the canvas yet.
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Put in a new group", "mdi-group", "Group name", "", "Create group"));

            case RemoveActionId when node is not null:
            {
                var going = model.Flows.Count(candidate => candidate.From == node.Id || candidate.To == node.Id);
                return going == 0
                    ? await DispatchAsync(target, new RemoveSupplyChainEntryCommand(body, id), cancellationToken)
                    : new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                        "Remove",
                        "mdi-delete-outline",
                        going == 1
                            ? "Removing this node also removes the 1 flow to or from it."
                            : $"Removing this node also removes the {going} flows to or from it.",
                        "Remove",
                        Danger: true));
            }

            case RemoveActionId when flow is not null || group is not null:
                // A group's members stay where they are, ungrouped, so nothing is lost to confirm.
                return await DispatchAsync(target, new RemoveSupplyChainEntryCommand(body, id), cancellationToken);

            case RenameActionId or IncreaseActionId or DecreaseActionId or GroupActionId or RemoveActionId:
                return new ContextExecutionFailed("That is no longer in this diagram.");

            default:
                return new ContextExecutionCompleted();
        }
    }

    /// <inheritdoc />
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(actionId == GroupActionId && string.IsNullOrWhiteSpace(value)
            ? ContextValidationResult.Rejected("A group needs a name.")
            : ContextValidationResult.Accepted);
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
        var model = _documents.GetOrLoad(body).Model;

        ICommand? command = actionId switch
        {
            RenameActionId when SupplyChainEdits.FlowOf(model, id) is not null => new SetSupplyChainPropertyCommand(body, id, SupplyChainKeys.Product, value),
            RenameActionId when SupplyChainEdits.NodeOf(model, id) is not null || SupplyChainEdits.GroupOf(model, id) is not null =>
                new SetSupplyChainPropertyCommand(body, id, SupplyChainKeys.Name, value),
            GroupActionId when SupplyChainEdits.NodeOf(model, id) is not null => new GroupSupplyChainNodeCommand(body, id, value),
            RemoveActionId when SupplyChainEdits.NodeOf(model, id) is not null => new RemoveSupplyChainEntryCommand(body, id),
            _ => null,
        };

        if (command is null)
        {
            return ContextCommitResult.Failed($"'{actionId}' does not apply to this selection.");
        }

        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

    /// <summary>An in-place edit over the element's own label - the name is what the canvas shows.</summary>
    private static ContextExecutionRequiresInput Input(string title, string field, string value, string confirm, string elementId) =>
        new(new ContextInputRequest(title, "mdi-pencil-outline", field, value, confirm, elementId));

    private async ValueTask<ContextExecutionResult> DispatchAsync(ContextTarget target, ICommand command, CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Result(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        ValueTask.FromResult(groups);
}
