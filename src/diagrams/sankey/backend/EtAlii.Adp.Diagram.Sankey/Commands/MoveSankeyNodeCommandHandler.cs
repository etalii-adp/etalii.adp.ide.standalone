using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// Moves a node's entry among the others, which is what moves it up or down its column: a column is
/// drawn in the order its nodes are written.
/// </summary>
public sealed class MoveSankeyNodeCommandHandler(ISankeyDocumentStore documents) : ICommandHandler<MoveSankeyNodeCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(MoveSankeyNodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return SankeyEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (SankeyEdits.NodeOf(model, command.NodeId) is not { } node || SankeyEdits.NodeOf(model, command.AnchorId) is not { } anchor)
            {
                return SankeyEdits.Gone();
            }

            return SankeyWriter.MoveNode(document, node, anchor, command.After);
        });
    }
}
