using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// Adds a node where it was dropped. Its column is the one nearest the drop, written down because a
/// node with no flows yet would otherwise fall to the first; its place in the column is above the
/// first node there whose middle is below the drop.
/// </summary>
public sealed class AddSankeyNodeCommandHandler(ISankeyDocumentStore documents) : SankeyEditHandler<AddSankeyNodeCommand>(documents)
{
    /// <inheritdoc />
    protected override async Task<CommandResult> RunAsync(AddSankeyNodeCommand command, CancellationToken cancellationToken)
    {
        var minted = command.NodeId.Length > 0 ? command : command with { NodeId = ShortGuid.NewShortGuid().ToString() };

        return await base.RunAsync(minted, cancellationToken);
    }

    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, SankeyModel model, AddSankeyNodeCommand command)
    {
        if (model.IsTaken(command.NodeId))
        {
            return EditOutcome.Refused("That id is already used in this diagram.");
        }

        var layout = SankeyLayout.Of(model);
        var column = SankeyLayout.ColumnAt(command.X);
        var below = column < layout.ColumnOrder.Count
            ? layout.ColumnOrder[column].FirstOrDefault(id => layout.Boxes[id].CenterY > command.Y)
            : null;

        var node = new SankeyNode(
            command.NodeId,
            UniqueName.Of(model.Nodes.Select(existing => existing.Label), "New node"),
            Color: "",
            Note: "",
            Format: "",
            Column: column > 0 ? column + 1 : null,
            Description: "",
            Range: new LineRange(0, 0));

        return SankeyWriter.AddNode(document, model, node, below is null ? null : model.NodeOf(below));
    }
}
