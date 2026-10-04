using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// Adds a node where it was dropped. Its column is the one nearest the drop, written down because a
/// node with no flows yet would otherwise fall to the first; its place in the column is above the
/// first node there whose middle is below the drop.
/// </summary>
public sealed class AddSankeyNodeCommandHandler(ISankeyDocumentStore documents) : ICommandHandler<AddSankeyNodeCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(AddSankeyNodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var minted = command.NodeId.Length > 0 ? command : command with { NodeId = ShortGuid.NewShortGuid().ToString() };

        return SankeyEdits.Run(documents, minted.BodyPath, minted, (document, model) =>
        {
            if (SankeyEdits.IsTaken(model, minted.NodeId))
            {
                return SankeyEdit.Refused("That id is already used in this diagram.");
            }

            var layout = SankeyLayout.Of(model);
            var column = SankeyLayout.ColumnAt(minted.X);
            var below = column < layout.ColumnOrder.Count
                ? layout.ColumnOrder[column].FirstOrDefault(id => layout.Boxes[id].CentreY > minted.Y)
                : null;

            var node = new SankeyNode(
                minted.NodeId,
                SankeyEdits.Unique(model.Nodes.Select(existing => existing.Label), "New node"),
                Color: "",
                Note: "",
                Format: "",
                Column: column > 0 ? column + 1 : null,
                Description: "",
                Range: new LineRange(0, 0));

            return SankeyWriter.AddNode(document, model, node, below is null ? null : SankeyEdits.NodeOf(model, below));
        });
    }
}
