using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Removes whatever the id names: a node with every flow to or from it, in one undo step, or a flow.</summary>
public sealed class RemoveSankeyEntryCommandHandler(ISankeyDocumentStore documents) : SankeyEditHandler<RemoveSankeyEntryCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(SankeyBody document, SankeyModel model, RemoveSankeyEntryCommand command)
    {
        if (model.NodeOf(command.EntryId) is { } node)
        {
            return SankeyEdits.RemoveNode(document, node);
        }

        return model.FlowOf(command.EntryId) is { } flow
            ? SankeyEdits.RemoveFlow(document, flow)
            : Gone;
    }
}
