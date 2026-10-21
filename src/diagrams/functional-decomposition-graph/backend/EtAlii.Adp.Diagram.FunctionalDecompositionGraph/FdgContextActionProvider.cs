using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
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
/// <b>What is offered is derived from the DISL definition</b> (<see cref="FdgDefinition.Menus"/>): its
/// context menus and the ids of its <c>x-fdg</c> block, and the deletion confirmation. What each action
/// does, what it asks and what it refuses stays here.
/// </para>
/// <para>
/// <b>Every action that needs no input dispatches here, in <see cref="RunAsync"/>.</b> A
/// Completed execution never reaches the commit leg, so an action answering Completed without
/// dispatching would have done nothing at all - the trap dependency-graph's provider records being
/// walked into three times.
/// </para>
/// <para>
/// <b>A connect is never trusted</b>: the command runs the type, cardinality and cycle checks
/// against the document as it is now, whatever the canvas that sent it believed.
/// </para>
/// </remarks>
public sealed class FdgContextActionProvider(IHistoryStackStore historyStacks, IFdgDocumentStore documents)
    : DiagramActionProvider<LineDocument, FdgModel>(historyStacks, documents)
{
    /// <summary>Rename an element in place - its Name, or a Comment's text.</summary>
    public const string RenameActionId = "fdg.rename";

    /// <summary>Rename a connection in place.</summary>
    private const string RenameConnectionActionId = "fdg.rename-connection";

    /// <summary>Remove an element and every connection to or from it, confirming when there are any.</summary>
    public const string RemoveActionId = "fdg.remove";

    /// <summary>Remove a connection.</summary>
    private const string DisconnectActionId = "fdg.disconnect";

    private const string AddPrefix = "fdg.add.";
    private const string ConnectPrefix = "fdg.connect.";

    /// <summary>The add action for one element type.</summary>
    public static string AddActionId(string elementType) => AddPrefix + elementType;

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <inheritdoc />
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(DocumentEntry<LineDocument, FdgModel> entry, string elementId) =>
        // Derived from the DISL definition: the element's or connection's, the empty canvas's or the
        // connect gesture's menu. Executing an action by id only finds actions its target discovers, so a
        // drop and a finished gesture each discover what may be executed against them.
        FdgDefinition.Menus(entry, elementId);

    /// <inheritdoc />
    protected override async ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target,
        DocumentEntry<LineDocument, FdgModel> entry,
        string actionId,
        CancellationToken cancellationToken)
    {
        var body = target.ResolvedFullPath;
        var model = entry.Model;
        var element = model.ElementOf(target.ElementId);

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

        return actionId switch
        {
            // In place, over the element's own label - multiline for a Comment, because its label is the
            // wrapped one.
            RenameActionId when element is not null => new ContextExecutionRequiresInput(new ContextInputRequest(
                element.IsComment ? "Edit text" : "Rename",
                "mdi-pencil-outline",
                element.IsComment ? "Text" : "Name",
                element.IsComment ? element.Text : element.Name,
                element.IsComment ? "Save" : "Rename",
                target.ElementId)),

            RenameConnectionActionId when model.ConnectionOf(target.ElementId) is { } connection => new ContextExecutionRequiresInput(new ContextInputRequest(
                "Rename", "mdi-pencil-outline", "Name", connection.Name, "Rename", target.ElementId)),

            // The definition's deletion confirmation: how many connections go with it, asked before it
            // runs; with none, no ceremony - and so the removal happens here.
            RemoveActionId when element is not null =>
                await RemoveAsync(target, new RemoveFdgElementCommand(body, element.Id), FdgDefinition.Confirmation(entry, target.ElementId), cancellationToken),

            DisconnectActionId when model.ConnectionOf(target.ElementId) is not null =>
                await DispatchAsync(target, new DisconnectFdgConnectionCommand(body, target.ElementId), cancellationToken),

            RenameActionId or RemoveActionId or RenameConnectionActionId or DisconnectActionId => new ContextExecutionFailed("That is no longer in this graph."),

            _ => new ContextExecutionCompleted(),
        };
    }

    /// <inheritdoc />
    protected override ICommand? CommitCommand(ContextTarget target, FdgModel model, string actionId, string value)
    {
        var body = target.ResolvedFullPath;
        var id = target.ElementId;
        var isElement = model.ElementOf(id) is not null;
        var isConnection = model.ConnectionOf(id) is not null;

        return actionId switch
        {
            RenameActionId when isElement => new RenameFdgElementCommand(body, id, value),
            RemoveActionId when isElement => new RemoveFdgElementCommand(body, id),
            RenameConnectionActionId when isConnection => new RenameFdgConnectionCommand(body, id, value),
            _ => null,
        };
    }
}
