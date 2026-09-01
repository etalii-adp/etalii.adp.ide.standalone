using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// What can be done to a selected timeline element or relation, offered as data and reached
/// through the one path the context service defines (Requirement 11.2).
/// </summary>
/// <remarks>
/// <para>
/// Every mutating action dispatches a command through the project's history, so each is one undo
/// away like every other edit in the IDE. <see cref="ConnectActionId"/> is the two-call one:
/// the context channel carries one element per call, and a relation needs two, so the first
/// call arms <see cref="TimelineConnectState"/> and the second - the next element clicked
/// (Requirement 11.6) - completes the pair and dispatches the command. Escape on the canvas
/// simply never sends the second call.
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

    /// <summary>The relation gesture: arm on the first element, complete on the second - or on a placement.</summary>
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

    /// <summary>How much later "after" is, and how long a freshly added element runs.</summary>
    private const int GapDays = 2;
    private const int NewElementDays = 7;

    private const string Gone = "That is no longer in this timeline.";

    private readonly IHistoryStackStore _historyStacks;
    private readonly ITimelineDocumentStore _documents;
    private readonly TimelineConnectState _connects;

    /// <summary>Creates the provider over the history, the one document store, and the relation gesture's state.</summary>
    public TimelineContextActionProvider(
        IHistoryStackStore historyStacks,
        ITimelineDocumentStore documents,
        TimelineConnectState connects)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(connects);

        _historyStacks = historyStacks;
        _documents = documents;
        _connects = connects;
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

        var model = _documents.GetOrLoad(target.ResolvedFullPath).Model;

        var element = TimelineEdits.ElementOf(model, target.ElementId);
        if (element is not null)
        {
            return Result(ForElement(element));
        }

        var relation = TimelineEdits.ConnectionOf(model, target.ElementId);
        if (relation is not null)
        {
            return Result(ForRelation());
        }

        if (TimelineNewPlacement.TryParse(target.ElementId, out _, out _))
        {
            // A placement discovers what can happen at empty canvas, because executing an action
            // by id only finds actions its target discovers - a drop or a relation completing
            // here resolves through this list.
            return Result(ForPlacement());
        }

        return Result([]);
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
                    target.ResolvedFullPath, begin, placedRow, period: actionId == AddElementActionId), cancellationToken);
            }

            case AddAfterActionId when element is not null:
            {
                // Tab: the next thing, a little later on the same row. Derived entirely from the
                // selected element, so nothing is asked here either.
                var anchor = element.End is { IsReadable: true } end ? end.Value!.Value
                    : element.Begin.IsReadable ? element.Begin.Value!.Value
                    : DateTimeOffset.UtcNow;
                return await DispatchAsync(target, NewElementAt(
                    target.ResolvedFullPath, anchor.AddDays(GapDays), element.Row, period: true), cancellationToken);
            }

            case AddBelowActionId when element is not null:
            {
                // Enter: the same stretch of time, one row down - a parallel track.
                var begin = element.Begin.IsReadable ? element.Begin.Value!.Value : DateTimeOffset.UtcNow;
                return await DispatchAsync(target, new AddTimelineElementCommand(
                    target.ResolvedFullPath,
                    ShortGuid.NewShortGuid().ToString(),
                    "New element",
                    TimelineScale.ToText(begin, TimelinePrecision.Date),
                    element.End?.Text,
                    element.Row + 1), cancellationToken);
            }

            case ConnectActionId when element is not null:
            {
                // The channel carries one element per call and a relation needs two, so the
                // gesture is two calls: the first arms, the second completes (Requirement 11.6).
                var pair = _connects.ArmOrComplete(target.WatchId, target.ResolvedFullPath, element.Id);
                if (pair is not { } completed)
                {
                    return new ContextExecutionCompleted();
                }

                return await DispatchAsync(target, new ConnectTimelineElementsCommand(
                    target.ResolvedFullPath, ShortGuid.NewShortGuid().ToString(), completed.From, completed.To, ""), cancellationToken);
            }

            case ConnectActionId when placed:
            {
                // The relation was dragged onto empty space: what it reaches does not exist yet,
                // so it is created where the gesture landed and related in the same command -
                // one history entry, one undo.
                var from = _connects.PendingFor(target.WatchId, target.ResolvedFullPath);
                if (from is null)
                {
                    return new ContextExecutionFailed("A relation starts from an element.");
                }

                _connects.Clear(target.WatchId, target.ResolvedFullPath);
                var begin = TimelineScale.ToTime(placedSeconds, TimelinePrecision.Date);
                return await DispatchAsync(target, new AddConnectedTimelineElementCommand(
                    target.ResolvedFullPath,
                    from,
                    ShortGuid.NewShortGuid().ToString(),
                    ShortGuid.NewShortGuid().ToString(),
                    TimelineScale.ToText(begin, TimelinePrecision.Date),
                    TimelineScale.ToText(begin.AddDays(NewElementDays), TimelinePrecision.Date),
                    placedRow), cancellationToken);
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
                    "Rename", "mdi-pencil-outline", "Label", element.Label, "Rename"));

            case RelabelActionId:
            {
                var relation = TimelineEdits.ConnectionOf(model, target.ElementId);
                return relation is null
                    ? new ContextExecutionFailed(Gone)
                    : new ContextExecutionRequiresInput(new ContextInputRequest(
                        "Relabel", "mdi-pencil-outline", "Label", relation.Label, "Relabel"));
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
                var going = TimelineWriter.ConnectionsTouching(model, element.Id).Count;
                if (going == 0)
                {
                    return await DispatchAsync(target,
                        new RemoveTimelineElementCommand(target.ResolvedFullPath, element.Id), cancellationToken);
                }

                return new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                    "Remove",
                    "mdi-delete-outline",
                    going == 1
                        ? "Removing this element also removes the 1 relation attached to it."
                        : $"Removing this element also removes the {going} relations attached to it.",
                    "Remove",
                    Danger: true));
            }

            case RemoveEndActionId when element is not null:
                return await DispatchAsync(target,
                    new SetTimelineEndCommand(target.ResolvedFullPath, element.Id, null), cancellationToken);

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
            // Validated as typed, so the dialog can refuse before the commit does - on exactly
            // the terms the handler will apply again (Requirement 3.4).
            if (TimelineInstants.Parse(value) is not { } end)
            {
                return ValueTask.FromResult(ContextValidationResult.Rejected($"'{value}' is not a time this timeline can read."));
            }

            var element = TimelineEdits.ElementOf(_documents.GetOrLoad(target.ResolvedFullPath).Model, target.ElementId);
            if (element is { Begin.IsReadable: true } && end < element.Begin.Value)
            {
                return ValueTask.FromResult(ContextValidationResult.Rejected("An element cannot end before it begins."));
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

    /// <summary>A freshly added element: born a week long, or a moment, at the given placement.</summary>
    /// <remarks>
    /// The ids are generated here, once, where the gesture happens - so the command instance the
    /// history holds carries them, and a redo re-creates under the ids it had. A week rather
    /// than zero length, because a zero-length element renders as an unreachable sliver and a
    /// week gives the adorners something to grab.
    /// </remarks>
    private static AddTimelineElementCommand NewElementAt(string body, DateTimeOffset begin, int row, bool period) =>
        new(
            body,
            ShortGuid.NewShortGuid().ToString(),
            period ? "New element" : "New moment",
            TimelineScale.ToText(begin, TimelinePrecision.Date),
            period ? TimelineScale.ToText(begin.AddDays(NewElementDays), TimelinePrecision.Date) : null,
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
            GiveEndActionId when isElement => new SetTimelineEndCommand(body, id, value),
            RemoveEndActionId when isElement => new SetTimelineEndCommand(body, id, null),
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

        var anchor = TimelineEdits.ElementOf(_documents.GetOrLoad(body).Model, anchorElementId);
        return NewElementAt(body, begin, anchor?.Row ?? 0, period);
    }

    private static IReadOnlyList<ContextActionGroupDefinition> ForElement(TimelineElement element)
    {
        List<ContextActionDefinition> edits =
        [
            new(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            new(ConnectActionId, "Relate…", "mdi-ray-start-arrow"),
        ];

        // For an element with an end, the end can be removed; for a moment, granted - the
        // gesture that is not there (no right adorner) is replaced by the action that is
        // (Requirement 7.5).
        edits.Add(element.IsPeriod
            ? new ContextActionDefinition(RemoveEndActionId, "Remove its end", "mdi-ray-start")
            : new ContextActionDefinition(GiveEndActionId, "Give it an end…", "mdi-ray-start-end"));

        edits.Add(new ContextActionDefinition(
            RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")));

        // The additions, as their own group so the menu separates changing this element from
        // adding the next - the mindmap's Insert/Enter pattern, on this type's two axes: after
        // in time, below in rows.
        List<ContextActionDefinition> additions =
        [
            new(AddAfterActionId, "Add element after", "mdi-arrow-expand-right", new ContextShortcutDefinition("Tab")),
            new(AddBelowActionId, "Add element below", "mdi-arrow-expand-down", new ContextShortcutDefinition("Enter")),
        ];

        return [new ContextActionGroupDefinition(edits), new ContextActionGroupDefinition(additions)];
    }

    /// <summary>What empty canvas offers: the two adds, and the completion of a relation gesture.</summary>
    private static IReadOnlyList<ContextActionGroupDefinition> ForPlacement() =>
    [
        new ContextActionGroupDefinition(
        [
            new ContextActionDefinition(AddElementActionId, "Add element here", "mdi-plus"),
            new ContextActionDefinition(AddMomentActionId, "Add moment here", "mdi-rhombus-medium"),
            new ContextActionDefinition(ConnectActionId, "Relate to a new element", "mdi-ray-start-arrow"),
        ]),
    ];

    private static IReadOnlyList<ContextActionGroupDefinition> ForRelation() =>
    [
        new ContextActionGroupDefinition(
        [
            new ContextActionDefinition(RelabelActionId, "Relabel…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            new ContextActionDefinition(DisconnectActionId, "Remove relation", "mdi-vector-polyline-remove", new ContextShortcutDefinition("Delete")),
        ]),
    ];

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Result(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        ValueTask.FromResult(groups);
}
