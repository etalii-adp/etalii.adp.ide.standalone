using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// What can be done to a selected node or flow, to empty canvas, and to a finished connect gesture -
/// offered as data, each answered by a command on the project's history.
/// </summary>
/// <remarks>
/// <para>
/// <b>The ids are the client's</b>, stated once in the client module's <c>sankeyIds.ts</c>; this
/// provider answers exactly those strings.
/// </para>
/// <para>
/// <b>What is offered is derived from the DISL definition</b> (<see cref="SankeyDefinition.Menus"/>): its
/// context menus, their availability and groups, and the ids of its <c>x-sankey</c> block. What each
/// action does, what it asks and what it refuses stays here.
/// </para>
/// </remarks>
public sealed class SankeyContextActionProvider(IHistoryStackStore historyStacks, ISankeyDocumentStore documents)
    : DiagramActionProvider<LineDocument, SankeyModel>(historyStacks, documents)
{
    /// <summary>Add a node where the canvas was dropped on or clicked.</summary>
    public const string AddActionId = "sankey.add";

    /// <summary>Draw a flow for a finished connect gesture.</summary>
    public const string ConnectActionId = "sankey.connect";

    /// <summary>Add one step to a flow's value.</summary>
    public const string IncreaseActionId = "sankey.increase";

    /// <summary>Take one step from a flow's value.</summary>
    public const string DecreaseActionId = "sankey.decrease";

    /// <summary>Rename a node.</summary>
    public const string RenameActionId = "sankey.rename";

    /// <summary>Remove a node with its flows, or a flow.</summary>
    public const string RemoveActionId = "sankey.remove";

    /// <summary>Draw every band and bar thicker.</summary>
    public const string ThickerActionId = "sankey.thicker";

    /// <summary>Draw every band and bar thinner.</summary>
    public const string ThinnerActionId = "sankey.thinner";

    /// <summary>Reorder every column's nodes so the bands cross least (<see cref="SankeyArrangement"/>).</summary>
    public const string ArrangeActionId = "sankey.arrange";

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <inheritdoc />
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(DocumentEntry<LineDocument, SankeyModel> entry, string elementId) =>
        // Derived from the DISL definition: a node's, a flow's, a placement's or a connect gesture's menu.
        SankeyDefinition.Menus(entry, elementId);

    /// <inheritdoc />
    protected override async ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target,
        DocumentEntry<LineDocument, SankeyModel> entry,
        string actionId,
        CancellationToken cancellationToken)
    {
        var body = target.ResolvedFullPath;
        var id = target.ElementId;
        var model = entry.Model;
        var node = model.NodeOf(id);
        var flow = model.FlowOf(id);

        return actionId switch
        {
            AddActionId => GestureIds.TryParsePlacement(id, out var x, out var y)
                ? await DispatchAsync(target, new AddSankeyNodeCommand(body, x, y), cancellationToken)
                : new ContextExecutionFailed("A node is added by dropping it where it belongs."),
            ConnectActionId => GestureIds.TryParseRelation(id, out var from, out var to)
                ? await DispatchAsync(target, new ConnectSankeyNodesCommand(body, from, to), cancellationToken)
                : new ContextExecutionFailed("A flow is drawn from one node to another."),
            ArrangeActionId => await DispatchAsync(target, new ArrangeSankeyCommand(body), cancellationToken),
            ThickerActionId or ThinnerActionId => await DispatchAsync(target, new ScaleSankeyThicknessCommand(body, actionId == ThickerActionId ? 1 : -1), cancellationToken),
            IncreaseActionId or DecreaseActionId when flow is not null => await DispatchAsync(target, new StepSankeyValueCommand(body, id, actionId == IncreaseActionId ? 1 : -1), cancellationToken),
            RenameActionId when node is not null => new ContextExecutionRequiresInput(new ContextInputRequest("Rename", "mdi-pencil-outline", "Name", node.Label, "Rename")),
            RemoveActionId when node is not null => await RemoveAsync(target, new RemoveSankeyEntryCommand(body, id), SankeyDefinition.RemovalConfirmation(model, id), cancellationToken),
            RemoveActionId when flow is not null => await DispatchAsync(target, new RemoveSankeyEntryCommand(body, id), cancellationToken),
            RenameActionId or IncreaseActionId or DecreaseActionId or RemoveActionId => new ContextExecutionFailed("That is no longer in this diagram."),
            _ => new ContextExecutionCompleted(),
        };
    }

    /// <inheritdoc />
    protected override ContextValidationResult Validate(string actionId, string value) =>
        actionId == RenameActionId && string.IsNullOrWhiteSpace(value)
            ? ContextValidationResult.Rejected("A node needs a name.")
            : ContextValidationResult.Accepted;

    /// <inheritdoc />
    protected override ICommand? CommitCommand(ContextTarget target, SankeyModel model, string actionId, string value) => actionId switch
    {
        RenameActionId when model.NodeOf(target.ElementId) is not null => new SetSankeyPropertyCommand(target.ResolvedFullPath, target.ElementId, SankeyKeys.Name, value),
        RemoveActionId when model.NodeOf(target.ElementId) is not null => new RemoveSankeyEntryCommand(target.ResolvedFullPath, target.ElementId),
        _ => null,
    };

}
