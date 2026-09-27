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

    /// <summary>Add a trigger at a drop or a placement.</summary>
    public const string AddTriggerActionId = "ghg.add.trigger";

    /// <summary>Add a note at a drop or a placement.</summary>
    public const string AddNoteActionId = "ghg.add.note";

    /// <summary>Draw an influence for a finished connect gesture.</summary>
    public const string ConnectActionId = "ghg.connect.influence";

    /// <summary>Rename a trend or trigger in place, or edit a note's text in place.</summary>
    public const string RenameActionId = "ghg.rename";

    /// <summary>Remove a trend or trigger and every influence touching it, or a note.</summary>
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

        if (GhgEdits.TriggerOf(model, target.ElementId) is not null)
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
                ]),
            ]);
        }

        if (GhgEdits.NoteOf(model, target.ElementId) is not null)
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameActionId, "Edit text…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
                ]),
            ]);
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
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(AddTrendActionId, "Add trend here", "mdi-plus"),
                    new ContextActionDefinition(AddTriggerActionId, "Add trigger here", "mdi-circle-slice-8"),
                    new ContextActionDefinition(AddNoteActionId, "Add note here", "mdi-note-text-outline"),
                ]),
            ]);
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
        var trigger = GhgEdits.TriggerOf(model, target.ElementId);
        var note = GhgEdits.NoteOf(model, target.ElementId);

        switch (actionId)
        {
            case AddTrendActionId:
                // The drop said everything an add needs, so nothing is asked.
                return GestureIds.TryParsePlacement(target.ElementId, out var x, out var y)
                    ? await DispatchAsync(target, new AddGhgTrendCommand(body, x, y), cancellationToken)
                    : new ContextExecutionFailed("A trend is added by dropping it where it starts.");

            case AddTriggerActionId:
                return GestureIds.TryParsePlacement(target.ElementId, out var triggerX, out var triggerY)
                    ? await DispatchAsync(target, new AddGhgTriggerCommand(body, triggerX, triggerY), cancellationToken)
                    : new ContextExecutionFailed("A trigger is added by dropping it where it happened.");

            case AddNoteActionId:
                // Added empty; the canvas opens its editor when it arrives, as the note's type declares.
                return GestureIds.TryParsePlacement(target.ElementId, out var noteX, out var noteY)
                    ? await DispatchAsync(target, new AddGhgNoteCommand(body, noteX, noteY), cancellationToken)
                    : new ContextExecutionFailed("A note is added by dropping it where it applies.");

            case ConnectActionId:
                // The whole gesture in one stateless call; the command refuses what the rules refuse.
                return GhgGestures.TryParseRelation(target.ElementId, out var from, out var fromEnd, out var to, out var toEnd)
                    ? await DispatchAsync(target, new AddGhgInfluenceCommand(body, from, fromEnd, to, toEnd), cancellationToken)
                    : new ContextExecutionFailed("An influence is drawn from one trend to another.");

            case RenameActionId when trend is not null || trigger is not null:
                // In place, over the element's own label - the name alone, never a trigger's date.
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Rename", "mdi-pencil-outline", "Name", trend?.Name ?? trigger!.Name, "Rename", target.ElementId));

            case RenameActionId when note is not null:
                // In place and multi-line, over the note's own wrapped text.
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Edit text", "mdi-pencil-outline", "Text", note.Text, "Save", target.ElementId));

            case EvenPhasesActionId when trend is not null:
                return await DispatchAsync(target, new ClearGhgBoundariesCommand(body, trend.Id), cancellationToken);

            case RemoveActionId when trend is not null || trigger is not null || note is not null:
            {
                // Says how many influences go with it before it runs; with none, no ceremony.
                var id = target.ElementId;
                var what = trend is not null ? "trend" : "trigger";
                var going = note is not null ? 0 : model.Influences.Count(influence => influence.From == id || influence.To == id);
                if (going == 0)
                {
                    return await DispatchAsync(target, new RemoveGhgElementCommand(body, id), cancellationToken);
                }

                return new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                    "Remove",
                    "mdi-delete-outline",
                    going == 1
                        ? $"Removing this {what} also removes the 1 influence to or from it."
                        : $"Removing this {what} also removes the {going} influences to or from it.",
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

        // The one value asked for is a name or a note's text, and the only wrong one is an empty name:
        // a note may be emptied.
        if (actionId != RenameActionId || !string.IsNullOrWhiteSpace(value))
        {
            return ValueTask.FromResult(ContextValidationResult.Accepted);
        }

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);
        return ValueTask.FromResult(
            entry.IsUsable && GhgEdits.NoteOf(entry.Model, target.ElementId) is not null
                ? ContextValidationResult.Accepted
                : ContextValidationResult.Rejected(entry.IsUsable && GhgEdits.TriggerOf(entry.Model, target.ElementId) is not null
                    ? "A trigger needs a name."
                    : "A trend needs a name."));
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
        var isElement = entry.IsUsable && GhgEdits.IsElement(entry.Model, id);

        ICommand? command = actionId switch
        {
            RenameActionId when isElement => new RenameGhgElementCommand(body, id, value),
            RemoveActionId when isElement => new RemoveGhgElementCommand(body, id),
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
