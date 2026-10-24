using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// Moves a node's entry among the others, which is what moves it up or down its column: a column is
/// drawn in the order its nodes are written.
/// </summary>
public sealed class MoveSankeyNodeCommandHandler(ISankeyDocumentStore documents) : SankeyEditHandler<MoveSankeyNodeCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(SankeyBody document, SankeyModel model, MoveSankeyNodeCommand command)
    {
        if (model.NodeOf(command.NodeId) is not { } node || model.NodeOf(command.AnchorId) is not { } anchor)
        {
            return Gone;
        }

        return SankeyEdits.MoveNode(document, node, anchor, command.After);
    }
}
