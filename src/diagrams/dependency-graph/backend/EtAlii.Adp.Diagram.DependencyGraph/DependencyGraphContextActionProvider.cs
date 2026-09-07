using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// What can be done to a selected node or dependency, offered as data and reached through the one
/// path the context service defines.
/// </summary>
/// <remarks>
/// <para>
/// Every mutating action dispatches a command through the project's history, so each is one undo
/// away like every other edit in the IDE. <see cref="ConnectActionId"/> arrives as one call
/// carrying the whole gesture in a <see cref="DependencyGraphRelationGesture"/> id - deliberately
/// stateless, because the two-call protocol it replaces kept an armed source between calls and
/// a stale arm made the next drag relate the wrong pair.
/// </para>
/// <para>
/// A gesture that lands on empty canvas has no element to name, so it names a
/// <see cref="DependencyGraphNewPlacement"/> instead - a synthetic id carrying where it landed. A
/// drop creates there; a relation dragged onto empty space creates there <b>and</b> relates, as
/// one command. That is what lets both gestures work through a channel with one element id per
/// call and no field for a position.
/// </para>
/// <para>
/// Two of the timeline's actions have no counterpart here: giving an element an end and taking one
/// away. A node has no end to give it. Nothing replaces them.
/// </para>
/// </remarks>
public sealed class DependencyGraphContextActionProvider : IContextActionProvider
{
    /// <summary>Rename, asking for the new label first.</summary>
    public const string RenameActionId = "dependencies.rename";

    /// <summary>Remove the node and its dependencies, confirming when there are any.</summary>
    public const string RemoveActionId = "dependencies.remove";

    /// <summary>The relation gesture, whole in one call: <c>rel:{from}-&gt;{to}</c>, the target a node or a placement.</summary>
    public const string ConnectActionId = "dependencies.connect";

    /// <summary>Remove a selected dependency.</summary>
    public const string DisconnectActionId = "dependencies.disconnect";

    /// <summary>Relabel a selected dependency, asking for the new label first.</summary>
    public const string RelabelActionId = "dependencies.relabel";

    /// <summary>Add a node. On a placement it lands there at once; elsewhere it asks for a label.</summary>
    public const string AddElementActionId = "dependencies.add-element";

    /// <summary>Add a node to the right of the selected one, on the same row, depending on it. Tab.</summary>
    public const string AddAfterActionId = "dependencies.add-after";

    /// <summary>Add a node below the selected one, at the same coordinate, depending on it. Enter.</summary>
    public const string AddBelowActionId = "dependencies.add-below";

    /// <summary>
    /// How far to the right "after" is, in canvas units.
    /// </summary>
    /// <remarks>
    /// A fixed step rather than the timeline's six-day gap, and the only arithmetic left in this
    /// class. Wide enough that the new node does not land under the one it grew from at the
    /// zoom levels the canvas opens at.
    /// </remarks>
    internal const double XStep = 240d;

    private const string Gone = "That is no longer in this graph.";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IDependencyGraphDocumentStore _documents;

    /// <summary>Creates the provider over the history and the one document store.</summary>
    public DependencyGraphContextActionProvider(
        IHistoryStackStore historyStacks,
        IDependencyGraphDocumentStore documents)
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

        var model = _documents.GetOrLoad(target.ResolvedFullPath).Model;

        if (DependencyGraphEdits.ElementOf(model, target.ElementId) is not null)
        {
            return Result(ForElement());
        }

        if (DependencyGraphEdits.RelationOf(model, target.ElementId) is not null)
        {
            return Result(ForRelation());
        }

        if (DependencyGraphNewPlacement.TryParse(target.ElementId, out _, out _))
        {
            // A placement discovers what can happen at empty canvas, because executing an action
            // by id only finds actions its target discovers - a drop resolves through this list.
            return Result(ForPlacement());
        }

        if (DependencyGraphRelationGesture.TryParse(target.ElementId, out _, out _))
        {
            // A finished relation gesture discovers its one action, for the same reason: the
            // canvas executes it by id against this target.
            return Result(
            [
                new ContextActionGroupDefinition(
                    [new ContextActionDefinition(ConnectActionId, "Depends on", "mdi-ray-start-arrow")]),
            ]);
        }

        return Result([]);
    }

    /// <inheritdoc />
    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        var model = _documents.GetOrLoad(target.ResolvedFullPath).Model;
        var element = DependencyGraphEdits.ElementOf(model, target.ElementId);
        var placed = DependencyGraphNewPlacement.TryParse(target.ElementId, out var placedX, out var placedRow);

        switch (actionId)
        {
            case AddElementActionId when placed:
            {
                // The gesture already said everything an add needs - where it landed - so
                // nothing is asked and the node appears where it was dropped.
                return await DispatchAsync(target, NewElementAt(
                    target.ResolvedFullPath, placedX, placedRow), cancellationToken);
            }

            case AddAfterActionId when element is not null:
            {
                // Tab: the next thing, a step to the right on the same row - and depending on
                // the one it grew from, because a node added from another is a thing that node
                // needs. Derived entirely from the selected node, so nothing is asked.
                return await DispatchAsync(target, NewRelatedElementAt(
                    target.ResolvedFullPath, element.Id, element.X + XStep, element.Row), cancellationToken);
            }

            case AddBelowActionId when element is not null:
            {
                // Enter: the same coordinate, one row down, and likewise depended upon - a second
                // thing the selected node needs, stacked under the first.
                return await DispatchAsync(target, NewRelatedElementAt(
                    target.ResolvedFullPath, element.Id, element.X, element.Row + 1), cancellationToken);
            }

            case ConnectActionId when DependencyGraphRelationGesture.TryParse(target.ElementId, out var from, out var to):
            {
                // The whole gesture in one call - from, and where it ended. Deliberately
                // stateless: the two-call protocol this replaces kept an armed source between
                // calls, and a stale arm made the next drag relate the wrong pair. Either end
                // may be a placement: a drag from the outgoing anchor lands its placement in
                // `to`, one from the incoming anchor arrives reversed with the placement in
                // `from` - what depends on a node points into it.
                if (DependencyGraphNewPlacement.TryParse(from, out var fromX, out var fromRow))
                {
                    if (DependencyGraphEdits.ElementOf(model, to) is null)
                    {
                        return new ContextExecutionFailed("The node this dependency reaches is no longer in this graph.");
                    }

                    // The new node is the relation's SOURCE: created at the drop, depending on
                    // the existing node.
                    return await DispatchAsync(target, new AddConnectedDependencyGraphElementCommand(
                        target.ResolvedFullPath,
                        to,
                        ShortGuid.NewShortGuid().ToString(),
                        ShortGuid.NewShortGuid().ToString(),
                        fromX,
                        fromRow,
                        NewElementIsSource: true), cancellationToken);
                }

                if (DependencyGraphEdits.ElementOf(model, from) is null)
                {
                    return new ContextExecutionFailed("The node this dependency starts from is no longer in this graph.");
                }

                if (DependencyGraphNewPlacement.TryParse(to, out var toX, out var toRow))
                {
                    // Released on empty canvas: what the node depends on does not exist yet, so
                    // it is created there and related in one command - one undo taking both.
                    return await DispatchAsync(target, new AddConnectedDependencyGraphElementCommand(
                        target.ResolvedFullPath,
                        from,
                        ShortGuid.NewShortGuid().ToString(),
                        ShortGuid.NewShortGuid().ToString(),
                        toX,
                        toRow), cancellationToken);
                }

                return await DispatchAsync(target, new ConnectDependencyGraphElementsCommand(
                    target.ResolvedFullPath, ShortGuid.NewShortGuid().ToString(), from, to, ""), cancellationToken);
            }

            case AddElementActionId:
                // No placement to land on - the action came from a menu - so the label is asked.
                // The timeline asked for a begin here because a begin was the one thing it could
                // not invent; a coordinate it can, and asking a user to type a canvas number
                // would be asking them to do the canvas's job.
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Add node",
                    "mdi-plus",
                    "Label",
                    NewElementLabel,
                    "Add"));

            case RenameActionId when element is not null:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Rename", "mdi-pencil-outline", "Label", element.Label, "Rename", target.ElementId));

            case RelabelActionId:
            {
                var relation = DependencyGraphEdits.RelationOf(model, target.ElementId);
                return relation is null
                    ? new ContextExecutionFailed(Gone)
                    : new ContextExecutionRequiresInput(new ContextInputRequest(
                        "Relabel", "mdi-pencil-outline", "Label", relation.Label, "Relabel", target.ElementId));
            }

            case RemoveActionId when element is not null:
            {
                // The action says how many dependencies go with it, before it runs. An
                // unconnected node needs no ceremony - and no ceremony means the removal happens
                // HERE: a Completed execution never reaches the commit leg, so an action that
                // answers Completed without dispatching has done nothing at all. That trap was
                // walked into three times in the module this was forked from; every action that
                // needs no input dispatches in this method.
                var going = DependencyGraphWriter.RelationsTouching(model, element.Id).Count;
                if (going == 0)
                {
                    return await DispatchAsync(target,
                        new RemoveDependencyGraphElementCommand(target.ResolvedFullPath, element.Id), cancellationToken);
                }

                return new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                    "Remove",
                    "mdi-delete-outline",
                    going == 1
                        ? "Removing this node also removes the 1 dependency attached to it."
                        : $"Removing this node also removes the {going} dependencies attached to it.",
                    "Remove",
                    Danger: true));
            }

            case DisconnectActionId when DependencyGraphEdits.RelationOf(model, target.ElementId) is not null:
                return await DispatchAsync(target,
                    new DisconnectDependencyGraphRelationCommand(target.ResolvedFullPath, target.ElementId), cancellationToken);

            default:
                return new ContextExecutionCompleted();
        }
    }

    /// <inheritdoc />
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        // Nothing to validate as typed. The timeline validated a date here on exactly the terms
        // its handler would apply again; every value this type asks for is a label, and a label
        // has no wrong answer.
        _ = actionId;
        _ = value;

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
        // other edit in the IDE.
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

    /// <summary>What a node created by a gesture is called until somebody renames it.</summary>
    private const string NewElementLabel = AddConnectedDependencyGraphElementCommandHandler.NewElementLabel;

    /// <summary>
    /// A freshly added node grown from another: created and depended upon, in one command.
    /// </summary>
    /// <remarks>
    /// The ids are generated here, once, where the gesture happens - so the command instance the
    /// history holds carries them, and a redo re-creates under the ids it had.
    /// </remarks>
    private static AddConnectedDependencyGraphElementCommand NewRelatedElementAt(
        string body, string fromElementId, double x, int row) =>
        new(
            body,
            fromElementId,
            ShortGuid.NewShortGuid().ToString(),
            ShortGuid.NewShortGuid().ToString(),
            x,
            row);

    private static AddDependencyGraphElementCommand NewElementAt(string body, double x, int row, string? label = null) =>
        new(
            body,
            ShortGuid.NewShortGuid().ToString(),
            string.IsNullOrWhiteSpace(label) ? NewElementLabel : label,
            x,
            row);

    private ICommand? CommandFor(ContextTarget target, string actionId, string value)
    {
        var body = target.ResolvedFullPath;
        var id = target.ElementId;

        // What kind of thing is selected decides which actions apply: a node-only action
        // committed against a dependency - or the other way round - answers "does not apply"
        // rather than a handler's guess at what went wrong.
        var model = _documents.GetOrLoad(body).Model;
        var element = DependencyGraphEdits.ElementOf(model, id);
        var isRelation = DependencyGraphEdits.RelationOf(model, id) is not null;

        return actionId switch
        {
            RenameActionId when element is not null => new RenameDependencyGraphElementCommand(body, id, value),
            RemoveActionId when element is not null => new RemoveDependencyGraphElementCommand(body, id),
            DisconnectActionId when isRelation => new DisconnectDependencyGraphRelationCommand(body, id),
            RelabelActionId when isRelation => new RelabelDependencyGraphRelationCommand(body, id, value),
            // The dialog path: the value is the label the user typed, and the position comes from
            // the node the gesture anchored on - a step to its right - or the origin on nothing.
            // Placement gestures never reach here; they complete in ExecuteAsync.
            AddElementActionId => NewElementAt(
                body,
                element is null ? 0d : element.X + XStep,
                element?.Row ?? 0,
                value),
            _ => null,
        };
    }

    private static IReadOnlyList<ContextActionGroupDefinition> ForElement()
    {
        List<ContextActionDefinition> edits =
        [
            new(RenameActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            new(RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
        ];

        // The additions, as their own group so the menu separates changing this node from adding
        // the next - the mindmap's Insert/Enter pattern, on this type's two axes: to the right,
        // and below.
        List<ContextActionDefinition> additions =
        [
            new(AddAfterActionId, "Add node to the right", "mdi-arrow-expand-right", new ContextShortcutDefinition("Tab")),
            new(AddBelowActionId, "Add node below", "mdi-arrow-expand-down", new ContextShortcutDefinition("Enter")),
        ];

        return [new ContextActionGroupDefinition(edits), new ContextActionGroupDefinition(additions)];
    }

    /// <summary>What empty canvas offers: the add, and the completion of a relation gesture.</summary>
    private static IReadOnlyList<ContextActionGroupDefinition> ForPlacement() =>
    [
        new ContextActionGroupDefinition(
        [
            new ContextActionDefinition(AddElementActionId, "Add node here", "mdi-plus"),
        ]),
    ];

    private static IReadOnlyList<ContextActionGroupDefinition> ForRelation() =>
    [
        new ContextActionGroupDefinition(
        [
            new ContextActionDefinition(RelabelActionId, "Relabel…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
            new ContextActionDefinition(DisconnectActionId, "Remove dependency", "mdi-vector-polyline-remove", new ContextShortcutDefinition("Delete")),
        ]),
    ];

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Result(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        ValueTask.FromResult(groups);
}
