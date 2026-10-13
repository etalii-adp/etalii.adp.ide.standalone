using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
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
/// <b>Every action that needs no input dispatches here, in <see cref="RunAsync"/></b>: a
/// Completed execution never reaches the commit leg, so answering Completed without dispatching
/// would do nothing at all.
/// </para>
/// </remarks>
public sealed class SupplyChainContextActionProvider(IHistoryStackStore historyStacks, ISupplyChainDocumentStore documents)
    : DiagramActionProvider<LineDocument, SupplyChainModel>(historyStacks, documents)
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

    /// <summary>The add action for one stage.</summary>
    public static string AddActionId(string stage) => AddPrefix + stage;

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <inheritdoc />
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(DocumentEntry<LineDocument, SupplyChainModel> entry, string elementId)
    {
        var model = entry.Model;
        var arrange = new ContextActionDefinition(
            ArrangeActionId, "Arrange diagram", "mdi-sitemap-outline", null,
            model.Nodes.Count > 0, "There is nothing to arrange until this diagram has a node.");

        if (model.NodeOf(elementId) is { } node)
        {
            var atZero = (node.Quantity ?? 0) <= 0;
            return
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
            ];
        }

        if (model.FlowOf(elementId) is { } flow)
        {
            var atZero = (flow.Volume ?? 0) <= 0;
            return
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
            ];
        }

        if (model.GroupOf(elementId) is not null)
        {
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(RemoveActionId, "Remove group", "mdi-ungroup", new ContextShortcutDefinition("Delete")),
                ]),
                new ContextActionGroupDefinition([arrange]),
            ];
        }

        // A drop, a background menu and a finished gesture each discover what may be executed
        // against them, because executing an action by id only finds actions its target discovers.
        if (GestureIds.TryParsePlacement(elementId, out _, out _))
        {
            return
            [
                new ContextActionGroupDefinition(
                    [.. SupplyChainNodeTypes.All.Select(stage => new ContextActionDefinition(AddActionId(stage), $"Add {SupplyChainNodeTypes.Display(stage).ToLowerInvariant()} here", SupplyChainStageIcons.Of(stage)))]),
                new ContextActionGroupDefinition([new ContextActionDefinition(AddGroupActionId, "Add group here", SupplyChainStageIcons.Group)]),
                new ContextActionGroupDefinition([arrange]),
            ];
        }

        return GestureIds.TryParseRelation(elementId, out _, out _)
            ? [new ContextActionGroupDefinition([new ContextActionDefinition(ConnectActionId, "Add flow", "mdi-arrow-right-thin")])]
            : [];
    }

    /// <inheritdoc />
    protected override async ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target,
        DocumentEntry<LineDocument, SupplyChainModel> entry,
        string actionId,
        CancellationToken cancellationToken)
    {
        var body = target.ResolvedFullPath;
        var id = target.ElementId;
        var model = entry.Model;
        var node = model.NodeOf(id);
        var flow = model.FlowOf(id);
        var group = model.GroupOf(id);

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
                var confirmation = going == 0
                    ? null
                    : new RemovalConfirmation(
                        "Remove",
                        going == 1
                            ? "Removing this node also removes the 1 flow to or from it."
                            : $"Removing this node also removes the {going} flows to or from it.",
                        "Remove");
                return await RemoveAsync(target, new RemoveSupplyChainEntryCommand(body, id), confirmation, cancellationToken);
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
    protected override ContextValidationResult Validate(string actionId, string value) =>
        actionId == GroupActionId && string.IsNullOrWhiteSpace(value)
            ? ContextValidationResult.Rejected("A group needs a name.")
            : ContextValidationResult.Accepted;

    /// <inheritdoc />
    protected override ICommand? CommitCommand(ContextTarget target, SupplyChainModel model, string actionId, string value)
    {
        var body = target.ResolvedFullPath;
        var id = target.ElementId;

        return actionId switch
        {
            RenameActionId when model.FlowOf(id) is not null => new SetSupplyChainPropertyCommand(body, id, SupplyChainKeys.Product, value),
            RenameActionId when model.NodeOf(id) is not null || model.GroupOf(id) is not null =>
                new SetSupplyChainPropertyCommand(body, id, SupplyChainKeys.Name, value),
            GroupActionId when model.NodeOf(id) is not null => new GroupSupplyChainNodeCommand(body, id, value),
            RemoveActionId when model.NodeOf(id) is not null => new RemoveSupplyChainEntryCommand(body, id),
            _ => null,
        };
    }

    /// <summary>An in-place edit over the element's own label - the name is what the canvas shows.</summary>
    private static ContextExecutionRequiresInput Input(string title, string field, string value, string confirm, string elementId) =>
        new(new ContextInputRequest(title, "mdi-pencil-outline", field, value, confirm, elementId));
}
