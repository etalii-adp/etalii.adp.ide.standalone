using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
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
/// <b>Every action that needs no input dispatches here, in <see cref="ExecuteAsync"/></b>: a
/// Completed execution never reaches the commit leg, so answering Completed without dispatching
/// would do nothing at all.
/// </para>
/// </remarks>
public sealed class SankeyContextActionProvider : IContextActionProvider
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

    private readonly IHistoryStackStore _historyStacks;
    private readonly ISankeyDocumentStore _documents;

    public SankeyContextActionProvider(IHistoryStackStore historyStacks, ISankeyDocumentStore documents)
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

        if (!Diagram.IsBody(target.ResolvedFullPath))
        {
            return Result([]);
        }

        var model = _documents.GetOrLoad(target.ResolvedFullPath).Model;
        var thickness = Thickness(model);

        if (SankeyEdits.NodeOf(model, target.ElementId) is not null)
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
                ]),
                thickness,
            ]);
        }

        if (SankeyEdits.FlowOf(model, target.ElementId) is { } flow)
        {
            var atZero = (flow.Value ?? 0) <= 0;
            return Result(
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
            ]);
        }

        // A drop, a background menu and a finished gesture each discover what may be executed
        // against them, because executing an action by id only finds actions its target discovers.
        if (GestureIds.TryParsePlacement(target.ElementId, out _, out _))
        {
            return Result(
            [
                new ContextActionGroupDefinition([new ContextActionDefinition(AddActionId, "Add node here", "mdi-plus")]),
                thickness,
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
        var node = SankeyEdits.NodeOf(model, id);
        var flow = SankeyEdits.FlowOf(model, id);

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
                return going == 0
                    ? await DispatchAsync(target, new RemoveSankeyEntryCommand(body, id), cancellationToken)
                    : new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                        "Remove",
                        "mdi-delete-outline",
                        going == 1
                            ? "Removing this node also removes the 1 flow to or from it."
                            : $"Removing this node also removes the {going} flows to or from it.",
                        "Remove",
                        Danger: true));
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
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(actionId == RenameActionId && string.IsNullOrWhiteSpace(value)
            ? ContextValidationResult.Rejected("A node needs a name.")
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
            RenameActionId when SankeyEdits.NodeOf(model, id) is not null => new SetSankeyPropertyCommand(body, id, SankeyKeys.Name, value),
            RemoveActionId when SankeyEdits.NodeOf(model, id) is not null => new RemoveSankeyEntryCommand(body, id),
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

    private async ValueTask<ContextExecutionResult> DispatchAsync(ContextTarget target, ICommand command, CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Result(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        ValueTask.FromResult(groups);
}
