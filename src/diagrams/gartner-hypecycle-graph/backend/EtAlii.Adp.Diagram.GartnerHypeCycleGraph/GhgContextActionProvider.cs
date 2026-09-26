using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// What can be done to a selected trend or influence, to empty canvas, and to a finished connect
/// gesture - offered as data, each answered by one command on the project's history (Requirement 11.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>The ids are the client's</b>, stated once in its <c>ghgIds.ts</c>, as FDG's are.
/// </para>
/// <para>
/// <b>Every action that needs no input dispatches here, in <see cref="ExecuteAsync"/></b>: a Completed
/// execution never reaches the commit leg, so answering Completed without dispatching would do nothing.
/// </para>
/// <para>
/// <b>A connect is never trusted</b>: the command runs the self and one-per-direction checks against
/// the document as it is now (Requirement 6.4).
/// </para>
/// <para>
/// <b>Read-only means nothing that edits is offered</b> (Requirement 11.4). The backend's read-only
/// case is a document that could not be read: its emptiness is not the document, so nothing may be
/// written over it, and the menus offer nothing rather than actions that would only refuse.
/// </para>
/// </remarks>
public sealed class GhgContextActionProvider : IContextActionProvider
{
    /// <summary>Add a trend at a drop or a placement.</summary>
    public const string AddTrendActionId = "ghg.add.trend";

    /// <summary>Draw an influence for a finished connect gesture.</summary>
    public const string ConnectActionId = "ghg.connect.influence";

    /// <summary>Rename a trend in place.</summary>
    public const string RenameActionId = "ghg.rename";

    /// <summary>Remove a trend and every influence touching it.</summary>
    public const string RemoveActionId = "ghg.remove";

    /// <summary>Return a trend's phases to even (Requirement 3.5).</summary>
    public const string EvenPhasesActionId = "ghg.even-phases";

    /// <summary>Remove an influence.</summary>
    public const string DisconnectActionId = "ghg.disconnect";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IGhgDocumentStore _documents;

    public GhgContextActionProvider(IHistoryStackStore historyStacks, IGhgDocumentStore documents)
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

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);
        if (!entry.IsUsable)
        {
            return Result([]);
        }

        var model = entry.Model;
        if (GhgEdits.TrendOf(model, target.ElementId) is { } trend)
        {
            List<ContextActionDefinition> actions =
            [
                new(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            ];
            if (trend.DraggedEnds.Any(boundary => boundary is not null))
            {
                actions.Add(new(EvenPhasesActionId, "Even phases", "mdi-arrow-split-vertical"));
            }

            actions.Add(new(RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")));
            return Result([new ContextActionGroupDefinition(actions)]);
        }

        if (GhgEdits.InfluenceOf(model, target.ElementId) is not null)
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(DisconnectActionId, "Remove influence", "mdi-vector-polyline-remove", new ContextShortcutDefinition("Delete")),
                ]),
            ]);
        }

        // Executing an action by id only finds actions its target discovers, so a drop and a finished
        // gesture each discover what may be executed against them.
        if (GestureIds.TryParsePlacement(target.ElementId, out _, out _))
        {
            return Result([new ContextActionGroupDefinition([new ContextActionDefinition(AddTrendActionId, "Add trend here", "mdi-plus")])]);
        }

        if (GhgGestures.TryParseRelation(target.ElementId, out _, out _, out _, out _))
        {
            return Result([new ContextActionGroupDefinition([new ContextActionDefinition(ConnectActionId, "Influence", "mdi-ray-start-arrow")])]);
        }

        return Result([]);
    }

    /// <inheritdoc />
    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        var body = target.ResolvedFullPath;
        var entry = _documents.GetOrLoad(body);
        if (!entry.IsUsable)
        {
            return new ContextExecutionFailed($"{System.IO.Path.GetFileName(body)} could not be read, so it cannot be edited.");
        }

        var model = entry.Model;
        var trend = GhgEdits.TrendOf(model, target.ElementId);

        switch (actionId)
        {
            case AddTrendActionId:
                // The drop said everything an add needs, so nothing is asked.
                return GestureIds.TryParsePlacement(target.ElementId, out var x, out var y)
                    ? await DispatchAsync(target, new AddGhgTrendCommand(body, x, y), cancellationToken)
                    : new ContextExecutionFailed("A trend is added by dropping it where it starts.");

            case ConnectActionId:
                // The whole gesture in one stateless call; the command refuses what the rules refuse.
                return GhgGestures.TryParseRelation(target.ElementId, out var from, out var fromEnd, out var to, out var toEnd)
                    ? await DispatchAsync(target, new AddGhgInfluenceCommand(body, from, fromEnd, to, toEnd), cancellationToken)
                    : new ContextExecutionFailed("An influence is drawn from one trend to another.");

            case RenameActionId when trend is not null:
                // In place, over the trend's own label.
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Rename", "mdi-pencil-outline", "Name", trend.Name, "Rename", target.ElementId));

            case EvenPhasesActionId when trend is not null:
                return await DispatchAsync(target, new ClearGhgBoundariesCommand(body, trend.Id), cancellationToken);

            case RemoveActionId when trend is not null:
            {
                // Says how many influences go with it before it runs; with none, no ceremony.
                var going = model.Influences.Count(influence => influence.From == trend.Id || influence.To == trend.Id);
                if (going == 0)
                {
                    return await DispatchAsync(target, new RemoveGhgTrendCommand(body, trend.Id), cancellationToken);
                }

                return new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                    "Remove",
                    "mdi-delete-outline",
                    going == 1
                        ? "Removing this trend also removes the 1 influence to or from it."
                        : $"Removing this trend also removes the {going} influences to or from it.",
                    "Remove",
                    Danger: true));
            }

            case DisconnectActionId when GhgEdits.InfluenceOf(model, target.ElementId) is not null:
                return await DispatchAsync(target, new RemoveGhgInfluenceCommand(body, target.ElementId), cancellationToken);

            case RenameActionId or RemoveActionId or EvenPhasesActionId or DisconnectActionId:
                return new ContextExecutionFailed("That is no longer in this graph.");

            default:
                return new ContextExecutionCompleted();
        }
    }

    /// <inheritdoc />
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        // The one value asked for is a name, and the only wrong one is an empty one.
        return ValueTask.FromResult(
            actionId == RenameActionId && string.IsNullOrWhiteSpace(value)
                ? ContextValidationResult.Rejected("A trend needs a name.")
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
        var entry = _documents.GetOrLoad(body);
        var isTrend = entry.IsUsable && GhgEdits.TrendOf(entry.Model, id) is not null;

        ICommand? command = actionId switch
        {
            RenameActionId when isTrend => new RenameGhgTrendCommand(body, id, value),
            RemoveActionId when isTrend => new RemoveGhgTrendCommand(body, id),
            _ => null,
        };

        if (command is null)
        {
            return ContextCommitResult.Failed($"'{actionId}' does not apply to this selection.");
        }

        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

    private async ValueTask<ContextExecutionResult> DispatchAsync(ContextTarget target, ICommand command, CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Result(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        ValueTask.FromResult(groups);
}
