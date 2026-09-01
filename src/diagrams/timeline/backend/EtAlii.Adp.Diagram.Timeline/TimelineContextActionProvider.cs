using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// What can be done to a selected timeline element or connection, offered as data and reached
/// through the one path the context service defines (Requirement 11.2).
/// </summary>
/// <remarks>
/// <para>
/// Every mutating action dispatches a command through the project's history, so each is one undo
/// away like every other edit in the IDE. <see cref="ConnectActionId"/> is the two-call one:
/// the context channel carries one element per call, and a connection needs two, so the first
/// call arms <see cref="TimelineConnectState"/> and the second - the next element clicked
/// (Requirement 11.6) - completes the pair and dispatches the command. Escape on the canvas
/// simply never sends the second call.
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

    /// <summary>Remove the element and its connections, confirming when there are any.</summary>
    public const string RemoveActionId = "timeline.remove";

    /// <summary>Enter the canvas's connect state; the next element clicked completes it.</summary>
    public const string ConnectActionId = "timeline.connect";

    /// <summary>Give a moment an end, asking for it first (Requirement 7.5).</summary>
    public const string GiveEndActionId = "timeline.give-end";

    /// <summary>Remove a period's end, making it a moment.</summary>
    public const string RemoveEndActionId = "timeline.remove-end";

    /// <summary>Disconnect a selected connection.</summary>
    public const string DisconnectActionId = "timeline.disconnect";

    /// <summary>Relabel a selected connection, asking for the new label first.</summary>
    public const string RelabelActionId = "timeline.relabel";

    /// <summary>Add a period, taking begin and row from the drop or click position.</summary>
    public const string AddPeriodActionId = "timeline.add-period";

    /// <summary>Add a moment, taking begin and row from the drop or click position.</summary>
    public const string AddMomentActionId = "timeline.add-moment";

    private const string Gone = "That is no longer in this timeline.";

    private readonly IHistoryStackStore _historyStacks;
    private readonly ITimelineDocumentStore _documents;
    private readonly TimelineConnectState _connects;

    /// <summary>Creates the provider over the history, the one document store, and the connect gesture's state.</summary>
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

        var connection = TimelineEdits.ConnectionOf(model, target.ElementId);
        if (connection is not null)
        {
            return Result(ForConnection());
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

        switch (actionId)
        {
            case ConnectActionId when element is not null:
            {
                // The channel carries one element per call and a connection needs two, so the
                // gesture is two calls: the first arms, the second completes (Requirement 11.6).
                // The canvas draws the pending curve from its own mirror of this state; Escape
                // simply never sends the second call.
                var pair = _connects.ArmOrComplete(target.WatchId, target.ResolvedFullPath, element.Id);
                if (pair is not { } completed)
                {
                    return new ContextExecutionCompleted();
                }

                var connect = await _historyStacks.Get(target.RootPath).ExecuteAsync(
                    new ConnectTimelineElementsCommand(
                        target.ResolvedFullPath, ShortGuid.NewShortGuid().ToString(), completed.From, completed.To, ""),
                    cancellationToken);
                return connect.IsSuccess
                    ? new ContextExecutionCompleted()
                    : new ContextExecutionFailed(connect.Error);
            }

            case AddPeriodActionId:
            case AddMomentActionId:
                // Asked before anything is written - the begin is the one fact an add cannot
                // guess. The row comes from the element the drop landed on, or 0 on nothing.
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    actionId == AddPeriodActionId ? "Add period" : "Add moment",
                    "mdi-plus",
                    "Begin",
                    TimelineScale.ToText(DateTimeOffset.UtcNow, TimelinePrecision.Date),
                    "Add"));

            case RenameActionId when element is not null:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Rename", "mdi-pencil-outline", "Label", element.Label, "Rename"));

            case RelabelActionId:
            {
                var connection = TimelineEdits.ConnectionOf(model, target.ElementId);
                return connection is null
                    ? new ContextExecutionFailed(Gone)
                    : new ContextExecutionRequiresInput(new ContextInputRequest(
                        "Relabel", "mdi-pencil-outline", "Label", connection.Label, "Relabel"));
            }

            case GiveEndActionId when element is not null:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Give it an end", "mdi-ray-start-end", "End", element.Begin.Text, "Set"));

            case RemoveActionId when element is not null:
            {
                // Requirement 2.5: the action says how many connections go with it, before it
                // runs. An unconnected element needs no ceremony.
                var going = TimelineWriter.ConnectionsTouching(model, element.Id).Count;
                return going == 0
                    ? new ContextExecutionCompleted()
                    : new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                        "Remove",
                        "mdi-delete-outline",
                        going == 1
                            ? "Removing this element also removes the 1 connection attached to it."
                            : $"Removing this element also removes the {going} connections attached to it.",
                        "Remove",
                        Danger: true));
            }

            default:
                // Connect completes with nothing dispatched (see the class remarks); everything
                // else has all it needs and commits straight away.
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

    private ICommand? CommandFor(ContextTarget target, string actionId, string value)
    {
        var body = target.ResolvedFullPath;
        var id = target.ElementId;

        // What kind of thing is selected decides which actions apply: an element-only action
        // committed against a connection - or the other way round - answers "does not apply"
        // rather than a handler's guess at what went wrong (Requirement 11.8).
        var model = _documents.GetOrLoad(body).Model;
        var isElement = TimelineEdits.ElementOf(model, id) is not null;
        var isConnection = TimelineEdits.ConnectionOf(model, id) is not null;

        return actionId switch
        {
            RenameActionId when isElement => new RenameTimelineElementCommand(body, id, value),
            RemoveActionId when isElement => new RemoveTimelineElementCommand(body, id),
            GiveEndActionId when isElement => new SetTimelineEndCommand(body, id, value),
            RemoveEndActionId when isElement => new SetTimelineEndCommand(body, id, null),
            DisconnectActionId when isConnection => new DisconnectTimelineConnectionCommand(body, id),
            RelabelActionId when isConnection => new RelabelTimelineConnectionCommand(body, id, value),
            // An add lands where the user pointed: the drop or click position travels in the
            // commit's value as "seconds,row" - the placement Requirement 9.3 refuses to discard.
            AddPeriodActionId => AddAt(body, id, value, period: true),
            AddMomentActionId => AddAt(body, id, value, period: false),
            _ => null,
        };
    }

    /// <summary>
    /// The add a dialog or a drop commits, at the placement its value carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two value forms. The dialog's is a plain time - the begin the user typed - and the row
    /// comes from the element the gesture anchored on, or 0 on nothing. The
    /// <c>seconds,row</c> form carries a full placement, and exists because the module is ready
    /// for a channel that can carry one; today's context-action flow only passes a value through
    /// a user dialog, which is the finding the closing check records.
    /// </para>
    /// <para>
    /// The id is generated here, once, where the gesture happens - so the command instance the
    /// history holds carries it, and a redo re-creates the element under the id it had. A period
    /// is born one week long: a zero-length period would render as an unreachable sliver, and a
    /// week gives the adorners something to grab.
    /// </para>
    /// </remarks>
    private ICommand? AddAt(string body, string anchorElementId, string value, bool period)
    {
        DateTimeOffset begin;
        int row;

        var parts = value.Split(',');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) &&
            int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedRow))
        {
            row = parsedRow;
            begin = TimelineScale.ToTime(seconds, TimelinePrecision.Date);
        }
        else if (TimelineInstants.Parse(value) is { } typed)
        {
            begin = typed;
            var anchor = TimelineEdits.ElementOf(_documents.GetOrLoad(body).Model, anchorElementId);
            row = anchor?.Row ?? 0;
        }
        else
        {
            return null;
        }

        var end = period
            ? TimelineScale.ToText(begin.AddDays(7), TimelinePrecision.Date)
            : null;

        return new AddTimelineElementCommand(
            body,
            ShortGuid.NewShortGuid().ToString(),
            period ? "New period" : "New moment",
            TimelineScale.ToText(begin, TimelinePrecision.Date),
            end,
            row);
    }

    private static IReadOnlyList<ContextActionGroupDefinition> ForElement(TimelineElement element)
    {
        List<ContextActionDefinition> edits =
        [
            new(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            new(ConnectActionId, "Connect…", "mdi-ray-start-arrow"),
        ];

        // For a period, the end can be removed; for a moment, granted - the gesture that is not
        // there (no right adorner) is replaced by the action that is (Requirement 7.5).
        edits.Add(element.IsPeriod
            ? new ContextActionDefinition(RemoveEndActionId, "Remove its end", "mdi-ray-start")
            : new ContextActionDefinition(GiveEndActionId, "Give it an end…", "mdi-ray-start-end"));

        edits.Add(new ContextActionDefinition(
            RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")));

        return [new ContextActionGroupDefinition(edits)];
    }

    private static IReadOnlyList<ContextActionGroupDefinition> ForConnection() =>
    [
        new ContextActionGroupDefinition(
        [
            new ContextActionDefinition(RelabelActionId, "Relabel…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            new ContextActionDefinition(DisconnectActionId, "Disconnect", "mdi-vector-polyline-remove", new ContextShortcutDefinition("Delete")),
        ]),
    ];

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Result(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        ValueTask.FromResult(groups);

    private static ValueTask<ContextExecutionResult> Execution(ContextExecutionResult result) =>
        ValueTask.FromResult(result);
}
