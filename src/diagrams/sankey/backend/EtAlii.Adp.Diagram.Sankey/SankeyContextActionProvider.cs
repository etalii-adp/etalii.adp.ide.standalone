using EtAlii.Adp.Context;
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
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(DocumentEntry<LineDocument, SankeyModel> entry, string elementId)
    {
        var model = entry.Model;
        var thickness = Thickness(model);

        if (model.NodeOf(elementId) is not null)
        {
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
                ]),
                thickness,
            ];
        }

        if (model.FlowOf(elementId) is { } flow)
        {
            var atZero = (flow.Value ?? 0) <= 0;
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(IncreaseActionId, "Increase value", "mdi-plus-circle-outline", new ContextShortcutDefinition("+")),
                    new ContextActionDefinition(DecreaseActionId, "Decrease value", "mdi-minus-circle-outline", new ContextShortcutDefinition("-"), !atZero, "The value is already zero."),
                ]),
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RemoveActionId, "Remove flow", "mdi-vector-polyline-remove", new ContextShortcutDefinition("Delete")),
                ]),
                thickness,
            ];
        }

        if (GestureIds.TryParsePlacement(elementId, out _, out _))
        {
            return
            [
                new ContextActionGroupDefinition([new ContextActionDefinition(AddActionId, "Add node here", "mdi-plus")]),
                thickness,
            ];
        }

        return GestureIds.TryParseRelation(elementId, out _, out _)
            ? [new ContextActionGroupDefinition([new ContextActionDefinition(ConnectActionId, "Add flow", "mdi-arrow-right-thin")])]
            : [];
    }

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

        switch (actionId)
        {
            case AddActionId:
                return GestureIds.TryParsePlacement(id, out var x, out var y)
                    ? await DispatchAsync(target, new AddSankeyNodeCommand(body, x, y), cancellationToken)
                    : new ContextExecutionFailed("A node is added by dropping it where it belongs.");

            case ConnectActionId:
                return GestureIds.TryParseRelation(id, out var from, out var to)
                    ? await DispatchAsync(target, new ConnectSankeyNodesCommand(body, from, to), cancellationToken)
                    : new ContextExecutionFailed("A flow is drawn from one node to another.");

            case ArrangeActionId:
                return await DispatchAsync(target, new ArrangeSankeyCommand(body), cancellationToken);

            case ThickerActionId or ThinnerActionId:
                return await DispatchAsync(target, new ScaleSankeyThicknessCommand(body, actionId == ThickerActionId ? 1 : -1), cancellationToken);

            case IncreaseActionId or DecreaseActionId when flow is not null:
                return await DispatchAsync(target, new StepSankeyValueCommand(body, id, actionId == IncreaseActionId ? 1 : -1), cancellationToken);

            case RenameActionId when node is not null:
                return new ContextExecutionRequiresInput(new ContextInputRequest("Rename", "mdi-pencil-outline", "Name", node.Label, "Rename"));

            case RemoveActionId when node is not null:
            {
                var going = model.Flows.Count(candidate => candidate.From == node.Id || candidate.To == node.Id);
                return await RemoveAsync(target, new RemoveSankeyEntryCommand(body, id), going == 0 ? null : new RemovalConfirmation(
                    "Remove",
                    going == 1
                        ? "Removing this node also removes the 1 flow to or from it."
                        : $"Removing this node also removes the {going} flows to or from it.",
                    "Remove"), cancellationToken);
            }

            case RemoveActionId when flow is not null:
                return await DispatchAsync(target, new RemoveSankeyEntryCommand(body, id), cancellationToken);

            case RenameActionId or IncreaseActionId or DecreaseActionId or RemoveActionId:
                return new ContextExecutionFailed("That is no longer in this diagram.");

            default:
                return new ContextExecutionCompleted();
        }
    }

    /// <inheritdoc />
    protected override ContextValidationResult Validate(ContextTarget target, string actionId, string value) =>
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

    /// <summary>
    /// What applies to the whole diagram, offered wherever the reader is so it stays in the ribbon:
    /// arranging it, and thicker and thinner, each disabled at its end of the range.
    /// </summary>
    private static ContextActionGroupDefinition Thickness(SankeyModel model) => new(
    [
        new ContextActionDefinition(
            ArrangeActionId, "Arrange diagram", "mdi-sitemap-outline", null,
            model.Nodes.Count > 0, "There is nothing to arrange until this diagram has a node."),
        new ContextActionDefinition(
            ThickerActionId, "Thicker bands", "mdi-arrow-expand-vertical", null,
            model.Settings.Thickness < SankeyGeometry.MaximumScale, "The bands are already as thick as they go."),
        new ContextActionDefinition(
            ThinnerActionId, "Thinner bands", "mdi-arrow-collapse-vertical", null,
            model.Settings.Thickness > SankeyGeometry.MinimumScale, "The bands are already as thin as they go."),
    ]);
}
