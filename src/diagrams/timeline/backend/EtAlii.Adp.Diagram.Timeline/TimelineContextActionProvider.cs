using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// What can be done to a selected timeline element or relation, offered as data and reached
/// through the one path the context service defines (Requirement 11.2).
/// </summary>
/// <remarks>
/// <para>
/// Every mutating action dispatches a command through the project's history, so each is one undo
/// away like every other edit in the IDE. <see cref="ConnectActionId"/> arrives as one call
/// carrying the whole gesture in a <see cref="TimelineRelationGesture"/> id - deliberately
/// stateless, because the two-call protocol it replaces kept an armed source between calls and
/// a stale arm made the next drag relate the wrong pair.
/// </para>
/// <para>
/// A gesture that lands on empty canvas has no element to name, so it names a
/// <see cref="TimelineNewPlacement"/> instead - a synthetic id carrying where it landed. A drop
/// creates there; a relation dragged onto empty space creates there <b>and</b> relates, as one
/// command. That is what lets both gestures work through a channel with one element id per call
/// and no field for a position.
/// </para>
/// <para>
/// Read-only mode has no seam a provider can ask yet - none of the shipped modules checks it
/// either. When one exists, this class is the single place the whole surface goes through
/// (Requirement 11.7), which is the reason everything funnels through it now.
/// </para>
/// </remarks>
public sealed class TimelineContextActionProvider : IContextActionProvider
{
    /// <summary>Rename, asking for the new label first.</summary>
    public const string RenameActionId = "timeline.rename";

    /// <summary>Remove the element and its relations, confirming when there are any.</summary>
    public const string RemoveActionId = "timeline.remove";

    /// <summary>The relation gesture, whole in one call: <c>rel:{from}-&gt;{to}</c>, the target an element or a placement.</summary>
    public const string ConnectActionId = "timeline.connect";

    /// <summary>Give a moment an end, asking for it first (Requirement 7.5).</summary>
    public const string GiveEndActionId = "timeline.give-end";

    /// <summary>Remove an element's end, making it a moment.</summary>
    public const string RemoveEndActionId = "timeline.remove-end";

    /// <summary>Remove a selected relation.</summary>
    public const string DisconnectActionId = "timeline.disconnect";

    /// <summary>Relabel a selected relation, asking for the new label first.</summary>
    public const string RelabelActionId = "timeline.relabel";

    /// <summary>Add an element. On a placement it lands there at once; elsewhere it asks for a begin.</summary>
    public const string AddElementActionId = "timeline.add-element";

    /// <summary>Add a moment. On a placement it lands there at once; elsewhere it asks for a begin.</summary>
    public const string AddMomentActionId = "timeline.add-moment";

    /// <summary>Add an element after the selected one, a little later on the same row. Tab.</summary>
    public const string AddAfterActionId = "timeline.add-after";

    /// <summary>Add an element below the selected one, at the same times on the next row. Enter.</summary>
    public const string AddBelowActionId = "timeline.add-below";

    /// <summary>Put every element on the row that keeps the diagram least cluttered (<see cref="TimelineArrangement"/>).</summary>
    public const string ArrangeActionId = "timeline.arrange";

    private const string Gone = "That is no longer in this timeline.";

    private readonly IHistoryStackStore _historyStacks;
    private readonly ITimelineDocumentStore _documents;

    /// <summary>Creates the provider over the history and the one document store.</summary>
    public TimelineContextActionProvider(
        IHistoryStackStore historyStacks,
        ITimelineDocumentStore documents)
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
            // Another type's element; a provider consulted for every element in its scope answers
            // with nothing rather than parsing another notation's file.
            return Result([]);
        }

        // Derived from the definition (TimelineDefinition.Menus): an element's and a relation's
        // menus, empty canvas at a placement - which a drop resolves its add through, because
        // executing an action by id only finds actions its target discovers - and a finished
        // relation gesture's one action, which the canvas executes by id against that target.
        // Arrange is offered wherever the reader is, which keeps it in the ribbon too.
        return Result(TimelineDefinition.Menus(_documents.GetOrLoad(target.ResolvedFullPath).Model, target.ElementId));
    }

    /// <inheritdoc />
    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        var model = _documents.GetOrLoad(target.ResolvedFullPath).Model;
        var element = TimelineEdits.ElementOf(model, target.ElementId);
        var placed = TimelineNewPlacement.TryParse(target.ElementId, out var placedSeconds, out var placedRow);

        switch (actionId)
        {
            case AddElementActionId when placed:
            case AddMomentActionId when placed:
            {
                // The gesture already said everything an add needs - where it landed - so
                // nothing is asked and the element appears where it was dropped.
                var begin = TimelineScale.ToTime(placedSeconds, TimelinePrecision.Date);
                return await DispatchAsync(target, NewElementAt(
                    model, target.ResolvedFullPath, begin, placedRow, period: actionId == AddElementActionId), cancellationToken);
            }

            case AddAfterActionId when element is not null:
            case AddBelowActionId when element is not null:
            {
                // Tab: the next thing, a little later on the same row; Enter: a parallel track, one
                // row down, from the same begin - each RELATED to the one it grew from, in one
                // command. Where it lands and what it is are the definition's addAfter and
                // addBelow, run on the selected element, so nothing is asked.
                (AddConnectedTimelineElementCommand? grown, string refusal) = TimelineDefinition.Grown(
                    model, target.ResolvedFullPath, actionId == AddAfterActionId ? "addAfter" : "addBelow", element.Id);
                return grown is null
                    ? new ContextExecutionFailed(refusal)
                    : await DispatchAsync(target, grown, cancellationToken);
            }

            case ConnectActionId when TimelineRelationGesture.TryParse(target.ElementId, out var from, out var to):
            {
                // The whole gesture in one call - from, and where it ended. Deliberately
                // stateless: the two-call protocol this replaces kept an armed source between
                // calls, and a stale arm made the next drag relate the wrong pair. Either end
                // may be a placement: a drag from the END anchor lands its placement in `to`,
                // one from the BEGIN anchor arrives reversed with the placement in `from` -
                // what precedes an element points into it.
                if (TimelineNewPlacement.TryParse(from, out var fromSeconds, out var fromRow))
                {
                    if (TimelineEdits.ElementOf(model, to) is null)
                    {
                        return new ContextExecutionFailed("The element this relation reaches is no longer in this timeline.");
                    }

                    // The new element is the relation's SOURCE: created at the drop, its end
                    // pointing into the existing element's start, as the relation tool's
                    // createSource makes it.
                    return await RelateHereAsync(target, model, to, "source", fromSeconds, fromRow, cancellationToken);
                }

                if (TimelineEdits.ElementOf(model, from) is null)
                {
                    return new ContextExecutionFailed("The element this relation starts from is no longer in this timeline.");
                }

                if (TimelineNewPlacement.TryParse(to, out var toSeconds, out var toRow))
                {
                    // Released on empty canvas: what the relation reaches does not exist yet, so
                    // it is created there and related in one command - one undo taking both - as
                    // the relation tool's createTarget makes it.
                    return await RelateHereAsync(target, model, from, "target", toSeconds, toRow, cancellationToken);
                }

                return await DispatchAsync(target, new ConnectTimelineElementsCommand(
                    target.ResolvedFullPath, ShortGuid.NewShortGuid().ToString(), from, to, ""), cancellationToken);
            }

            case AddElementActionId:
            case AddMomentActionId:
                // No placement to land on - the action came from a menu - so the begin is asked,
                // and the row comes from the element the gesture anchored on, or 0 on nothing.
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    actionId == AddElementActionId ? "Add element" : "Add moment",
                    "mdi-plus",
                    "Begin",
                    TimelineScale.ToText(DateTimeOffset.UtcNow, TimelinePrecision.Date),
                    "Add"));

            case RenameActionId when element is not null:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Rename", "mdi-pencil-outline", "Label", element.Label, "Rename", target.ElementId));

            case RelabelActionId:
            {
                var relation = TimelineEdits.ConnectionOf(model, target.ElementId);
                return relation is null
                    ? new ContextExecutionFailed(Gone)
                    : new ContextExecutionRequiresInput(new ContextInputRequest(
                        "Relabel", "mdi-pencil-outline", "Label", relation.Label, "Relabel", target.ElementId));
            }

            case GiveEndActionId when element is not null:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Give it an end", "mdi-ray-start-end", "End", element.Begin.Text, "Set"));

            case RemoveActionId when element is not null:
            {
                // Requirement 2.5: the action says how many relations go with it, before it
                // runs. An unrelated element needs no ceremony - and no ceremony means the
                // removal happens HERE: a Completed execution never reaches the commit leg, so
                // an action that answers Completed without dispatching has done nothing at all.
                // That trap has now been walked into three times in this module; every action
                // that needs no input dispatches in this method.
                // Whether it asks, and what, is the definition's deletion policy.
                if (TimelineDefinition.RemoveConfirmation(model, target.ElementId) is not { } confirmation)
                {
                    return await DispatchAsync(target,
                        new RemoveTimelineElementCommand(target.ResolvedFullPath, element.Id), cancellationToken);
                }

                return new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                    confirmation.Title,
                    "mdi-delete-outline",
                    confirmation.Message,
                    confirmation.ConfirmLabel,
                    Danger: confirmation.Danger));
            }

            case ArrangeActionId:
                return await DispatchAsync(target, new ArrangeTimelineCommand(target.ResolvedFullPath), cancellationToken);

            case RemoveEndActionId when element is not null:
            {
                // The definition's removeEnd: the end unset and the period made a moment. A moment,
                // which it is not for, has no end to remove; that stays the one recorded step that
                // changes nothing it always was, rather than a refusal.
                (SetTimelineEndCommand? removal, string refusal) = TimelineDefinition.EndChange(model, target.ResolvedFullPath, "removeEnd", element.Id, null);
                return refusal.Length > 0
                    ? new ContextExecutionFailed(refusal)
                    : await DispatchAsync(target, removal ?? new SetTimelineEndCommand(target.ResolvedFullPath, element.Id, null), cancellationToken);
            }

            case DisconnectActionId when TimelineEdits.ConnectionOf(model, target.ElementId) is not null:
                return await DispatchAsync(target,
                    new DisconnectTimelineConnectionCommand(target.ResolvedFullPath, target.ElementId), cancellationToken);

            default:
                return new ContextExecutionCompleted();
        }
    }

    /// <inheritdoc />
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (actionId == GiveEndActionId)
        {
            // Validated as typed, so the dialog can refuse before the commit does - by running the
            // definition's giveEnd, whose refusals the handler applies again (Requirement 3.4).
            (_, string refusal) = TimelineDefinition.EndChange(
                _documents.GetOrLoad(target.ResolvedFullPath).Model, target.ResolvedFullPath, "giveEnd", target.ElementId, value);
            if (refusal.Length > 0)
            {
                return ValueTask.FromResult(ContextValidationResult.Rejected(refusal));
            }
        }

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

        var command = CommandFor(target, actionId, value);
        if (command is null)
        {
            return ContextCommitResult.Failed($"'{actionId}' does not apply to this selection.");
        }

        // Through the project's history, so every one of these is one undo away like every
        // other edit in the IDE (Requirement 11.1).
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

    private async ValueTask<ContextExecutionResult> DispatchAsync(ContextTarget target, ICommand command, CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess
            ? new ContextExecutionCompleted()
            : new ContextExecutionFailed(result.Error);
    }

    /// <summary>
    /// A relation gesture released on empty canvas at <paramref name="seconds"/> and <paramref name="row"/>:
    /// the element the definition's relation tool creates there, the date under the pointer as its begin,
    /// related to <paramref name="elementId"/> as the gesture's <paramref name="newEnd"/>, in one command.
    /// </summary>
    private async ValueTask<ContextExecutionResult> RelateHereAsync(
        ContextTarget target, TimelineModel model, string elementId, string newEnd, double seconds, int row, CancellationToken cancellationToken)
    {
        var begin = TimelineScale.ToText(TimelineScale.ToTime(seconds, TimelinePrecision.Date), TimelinePrecision.Date);
        (AddConnectedTimelineElementCommand? related, string refusal) = TimelineDefinition.RelatedHere(model, target.ResolvedFullPath, elementId, newEnd, begin, row);
        return related is null
            ? new ContextExecutionFailed(refusal)
            : await DispatchAsync(target, related, cancellationToken);
    }

    /// <summary>
    /// A freshly added element, a period or a moment, at the given placement: the definition's
    /// <c>addElementHere</c> or <c>addMomentHere</c>, its begin the date of <paramref name="begin"/>.
    /// </summary>
    /// <remarks>
    /// The id is generated here, once, where the gesture happens - so the command instance the
    /// history holds carries it, and a redo re-creates under the id it had.
    /// </remarks>
    private static AddTimelineElementCommand NewElementAt(TimelineModel model, string body, DateTimeOffset begin, int row, bool period) =>
        TimelineDefinition.Addition(
            model,
            body,
            period ? "addElementHere" : "addMomentHere",
            ShortGuid.NewShortGuid().ToString(),
            TimelineScale.ToText(begin, TimelinePrecision.Date),
            row);

    private ICommand? CommandFor(ContextTarget target, string actionId, string value)
    {
        var body = target.ResolvedFullPath;
        var id = target.ElementId;

        // What kind of thing is selected decides which actions apply: an element-only action
        // committed against a relation - or the other way round - answers "does not apply"
        // rather than a handler's guess at what went wrong (Requirement 11.8).
        var model = _documents.GetOrLoad(body).Model;
        var isElement = TimelineEdits.ElementOf(model, id) is not null;
        var isRelation = TimelineEdits.ConnectionOf(model, id) is not null;

        return actionId switch
        {
            RenameActionId when isElement => new RenameTimelineElementCommand(body, id, value),
            RemoveActionId when isElement => new RemoveTimelineElementCommand(body, id),
            GiveEndActionId when isElement => TimelineDefinition.EndChange(model, body, "giveEnd", id, value).Command,
            RemoveEndActionId when isElement => TimelineDefinition.EndChange(model, body, "removeEnd", id, null) is (var removal, { Length: 0 })
                ? removal ?? new SetTimelineEndCommand(body, id, null)
                : null,
            DisconnectActionId when isRelation => new DisconnectTimelineConnectionCommand(body, id),
            RelabelActionId when isRelation => new RelabelTimelineConnectionCommand(body, id, value),
            // The dialog path: the value is the begin the user typed, and the row comes from the
            // element the gesture anchored on, or 0 on nothing. Placement gestures never reach
            // here - they complete in ExecuteAsync.
            AddElementActionId => AddAt(body, id, value, period: true),
            AddMomentActionId => AddAt(body, id, value, period: false),
            _ => null,
        };
    }

    private ICommand? AddAt(string body, string anchorElementId, string value, bool period)
    {
        if (TimelineInstants.Parse(value) is not { } begin)
        {
            return null;
        }

        var model = _documents.GetOrLoad(body).Model;
        var anchor = TimelineEdits.ElementOf(model, anchorElementId);
        return NewElementAt(model, body, begin, anchor?.Row ?? 0, period);
    }

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Result(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        ValueTask.FromResult(groups);
}
