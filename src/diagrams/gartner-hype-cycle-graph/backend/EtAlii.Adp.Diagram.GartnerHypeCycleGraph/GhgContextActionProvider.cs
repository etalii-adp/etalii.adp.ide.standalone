using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

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
/// <b>What is offered is derived from the DISL definition</b> (<see cref="GhgDefinition.Menus"/>): its
/// context menus, their visibility, availability and groups, and the ids of its <c>x-ghg</c> block.
/// What each action does, what it asks and what it refuses stays here.
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
public sealed class GhgContextActionProvider(IHistoryStackStore historyStacks, IGhgDocumentStore documents)
    : DiagramActionProvider<GhgBody, GhgModel>(historyStacks, documents)
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

    /// <summary>Put every trend, trigger and note on the row that keeps the graph least cluttered (<see cref="GhgArrangement"/>).</summary>
    public const string ArrangeActionId = "ghg.arrange";

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <inheritdoc />
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(DocumentEntry<GhgBody, GhgModel> entry, string elementId) =>
        // Derived from the DISL definition: the element's, the empty canvas's or the connect gesture's
        // menu, nothing offered on a read-only entry. Executing an action by id only finds actions its
        // target discovers, so a drop and a finished gesture each discover what may be executed against them.
        GhgDefinition.Menus(entry, elementId);

    /// <inheritdoc />
    protected override async ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target,
        DocumentEntry<GhgBody, GhgModel> entry,
        string actionId,
        CancellationToken cancellationToken)
    {
        var body = target.ResolvedFullPath;
        var model = entry.Model;
        var trend = model.TrendOf(target.ElementId);
        var trigger = model.TriggerOf(target.ElementId);
        var note = model.NoteOf(target.ElementId);

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
                // The definition's deletion confirmation: how many influences go with it, asked before
                // it runs; with none, no ceremony.
                var id = target.ElementId;
                var confirmation = GhgDefinition.ElementOf(entry.Document.Disl.Diagram, id) is { } element
                    ? DeletionPolicy.Confirmation(GhgDefinition.Specification, element, env: GhgDefinition.EditEnv)
                    : null;
                return await RemoveAsync(target, new RemoveGhgElementCommand(body, id), confirmation is null ? null : new RemovalConfirmation(
                    confirmation.Title, confirmation.Message, confirmation.ConfirmLabel, confirmation.Danger), cancellationToken);
            }

            case DisconnectActionId when model.InfluenceOf(target.ElementId) is not null:
                return await DispatchAsync(target, new RemoveGhgInfluenceCommand(body, target.ElementId), cancellationToken);

            case ArrangeActionId:
                return await DispatchAsync(target, new ArrangeGhgCommand(body), cancellationToken);

            case RenameActionId or RemoveActionId or EvenPhasesActionId or DisconnectActionId:
                return new ContextExecutionFailed("That is no longer in this graph.");

            default:
                return new ContextExecutionCompleted();
        }
    }

    /// <inheritdoc />
    protected override ContextValidationResult Validate(ContextTarget target, string actionId, string value)
    {
        // The one value asked for is a name or a note's text, and the only wrong one is an empty name:
        // a note may be emptied.
        if (actionId != RenameActionId || !string.IsNullOrWhiteSpace(value))
        {
            return ContextValidationResult.Accepted;
        }

        var entry = Load(target);
        return entry.IsUsable && entry.Model.NoteOf(target.ElementId) is not null
            ? ContextValidationResult.Accepted
            : ContextValidationResult.Rejected(entry.IsUsable && entry.Model.TriggerOf(target.ElementId) is not null
                ? "A trigger needs a name."
                : "A trend needs a name.");
    }

    /// <inheritdoc />
    protected override ICommand? CommitCommand(ContextTarget target, GhgModel model, string actionId, string value)
    {
        var body = target.ResolvedFullPath;
        var id = target.ElementId;
        var isElement = model.IsElement(id);

        return actionId switch
        {
            RenameActionId when isElement => new RenameGhgElementCommand(body, id, value),
            RemoveActionId when isElement => new RemoveGhgElementCommand(body, id),
            _ => null,
        };
    }
}
