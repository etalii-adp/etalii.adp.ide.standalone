using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// What can be done to a selected element or connection, to empty canvas, and to a finished connect
/// gesture - offered as data, each answered by one of task 12's commands on the project's history.
/// </summary>
/// <remarks>
/// <para>
/// <b>The ids are the client's</b>, stated once in the client module's <c>fdgIds.ts</c>: add and
/// connect are one id per type (<c>fdg.add.{type}</c>, <c>fdg.connect.{relation}</c>), so a target
/// keeps the two shapes the shared <see cref="GestureIds"/> reads and the type is never parsed out
/// of it.
/// </para>
/// <para>
/// <b>Every action that needs no input dispatches here, in <see cref="ExecuteAsync"/>.</b> A
/// Completed execution never reaches the commit leg, so an action answering Completed without
/// dispatching would have done nothing at all - the trap dependency-graph's provider records being
/// walked into three times.
/// </para>
/// <para>
/// <b>A connect is never trusted</b>: the command runs the type, cardinality and cycle checks
/// against the document as it is now, whatever the canvas that sent it believed.
/// </para>
/// </remarks>
public sealed class FdgContextActionProvider : IContextActionProvider
{
    /// <summary>Rename an element in place - its Name, or a Comment's text.</summary>
    public const string RenameActionId = "fdg.rename";

    /// <summary>Rename a connection in place.</summary>
    public const string RenameConnectionActionId = "fdg.rename-connection";

    /// <summary>Remove an element and every connection to or from it, confirming when there are any.</summary>
    public const string RemoveActionId = "fdg.remove";

    /// <summary>Remove a connection.</summary>
    public const string DisconnectActionId = "fdg.disconnect";

    private const string AddPrefix = "fdg.add.";
    private const string ConnectPrefix = "fdg.connect.";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IFdgDocumentStore _documents;

    public FdgContextActionProvider(IHistoryStackStore historyStacks, IFdgDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    /// <summary>The add action for one element type.</summary>
    public static string AddActionId(string elementType) => AddPrefix + elementType;

    /// <summary>The connect action for one relation.</summary>
    public static string ConnectActionId(string relation) => ConnectPrefix + relation;

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Diagram.IsBody(target.ResolvedFullPath))
        {
            // Another type's element: a provider consulted for every element in its scope answers
            // with nothing rather than parsing another notation's file.
            return Result([]);
        }

        var model = _documents.GetOrLoad(target.ResolvedFullPath).Model;

        if (FdgEdits.ElementOf(model, target.ElementId) is { } element)
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameActionId, element.IsComment ? "Edit text…" : "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(RemoveActionId, "Remove", "mdi-delete-outline", new ContextShortcutDefinition("Delete")),
                ]),
            ]);
        }

        if (FdgEdits.ConnectionOf(model, target.ElementId) is not null)
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameConnectionActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(DisconnectActionId, "Remove connection", "mdi-vector-polyline-remove", new ContextShortcutDefinition("Delete")),
                ]),
            ]);
        }

        // Executing an action by id only finds actions its target discovers, so a drop and a
        // finished gesture each discover what may be executed against them.
        if (GestureIds.TryParsePlacement(target.ElementId, out _, out _))
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                    [.. FdgElementTypes.All.Select(type => new ContextActionDefinition(AddActionId(type), $"Add {Titled(type)} here", "mdi-plus"))]),
            ]);
        }

        if (GestureIds.TryParseRelation(target.ElementId, out _, out _))
        {
            return Result(
            [
                new ContextActionGroupDefinition(
                    [.. FdgRelations.All.Select(relation => new ContextActionDefinition(ConnectActionId(relation.Id), Titled(relation.Id), "mdi-ray-start-arrow"))]),
            ]);
        }

        return Result([]);
    }

    /// <inheritdoc />
    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        var body = target.ResolvedFullPath;
        var model = _documents.GetOrLoad(body).Model;
        var element = FdgEdits.ElementOf(model, target.ElementId);

        if (actionId.StartsWith(AddPrefix, StringComparison.Ordinal))
        {
            var type = actionId[AddPrefix.Length..];
            if (!FdgElementTypes.IsKnown(type) || !GestureIds.TryParsePlacement(target.ElementId, out var x, out var y))
            {
                return new ContextExecutionFailed("An element is added by dropping it where it belongs.");
            }

            // The drop said everything an add needs - the type and the centre it landed at - so
            // nothing is asked.
            return await DispatchAsync(target, new AddFdgElementCommand(body, type, x, y), cancellationToken);
        }

        if (actionId.StartsWith(ConnectPrefix, StringComparison.Ordinal))
        {
            var relation = actionId[ConnectPrefix.Length..];
            if (!GestureIds.TryParseRelation(target.ElementId, out var from, out var to))
            {
                return new ContextExecutionFailed("A connection is drawn from one element to another.");
            }

            // The whole gesture in one stateless call; the command refuses what the rules refuse.
            return await DispatchAsync(target, new ConnectFdgElementsCommand(body, relation, from, to), cancellationToken);
        }

        switch (actionId)
        {
            case RenameActionId when element is not null:
                // In place, over the element's own label - multiline for a Comment, because its
                // label is the wrapped one.
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    element.IsComment ? "Edit text" : "Rename",
                    "mdi-pencil-outline",
                    element.IsComment ? "Text" : "Name",
                    element.IsComment ? element.Text : element.Name,
                    element.IsComment ? "Save" : "Rename",
                    target.ElementId));

            case RenameConnectionActionId when FdgEdits.ConnectionOf(model, target.ElementId) is { } connection:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Rename", "mdi-pencil-outline", "Name", connection.Name, "Rename", target.ElementId));

            case RemoveActionId when element is not null:
            {
                // Says how many connections go with it before it runs; with none, no ceremony -
                // and so the removal happens here.
                var going = model.Connections.Count(connection => connection.From == element.Id || connection.To == element.Id);
                if (going == 0)
                {
                    return await DispatchAsync(target, new RemoveFdgElementCommand(body, element.Id), cancellationToken);
                }

                return new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                    "Remove",
                    "mdi-delete-outline",
                    going == 1
                        ? "Removing this element also removes the 1 connection to or from it."
                        : $"Removing this element also removes the {going} connections to or from it.",
                    "Remove",
                    Danger: true));
            }

            case DisconnectActionId when FdgEdits.ConnectionOf(model, target.ElementId) is not null:
                return await DispatchAsync(target, new DisconnectFdgConnectionCommand(body, target.ElementId), cancellationToken);

            case RenameActionId or RemoveActionId or RenameConnectionActionId or DisconnectActionId:
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

        // Every value asked for is a name or a Comment's text, and neither has a wrong answer; the
        // command refuses anything the document cannot hold.
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

        var body = target.ResolvedFullPath;
        var id = target.ElementId;
        var model = _documents.GetOrLoad(body).Model;
        var isElement = FdgEdits.ElementOf(model, id) is not null;
        var isConnection = FdgEdits.ConnectionOf(model, id) is not null;

        ICommand? command = actionId switch
        {
            RenameActionId when isElement => new RenameFdgElementCommand(body, id, value),
            RemoveActionId when isElement => new RemoveFdgElementCommand(body, id),
            RenameConnectionActionId when isConnection => new RenameFdgConnectionCommand(body, id, value),
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

    /// <summary>A document word as a menu reads it: <c>owns-data</c> becomes "owns data".</summary>
    private static string Titled(string word) => word.Replace('-', ' ');

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Result(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        ValueTask.FromResult(groups);
}
