using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Removes whatever the id names.</summary>
public sealed class RemoveSankeyEntryCommandHandler(ISankeyDocumentStore documents) : SankeyEditHandler<RemoveSankeyEntryCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument document, SankeyModel model, RemoveSankeyEntryCommand command)
    {
        if (model.NodeOf(command.EntryId) is { } node)
        {
            return SankeyWriter.RemoveNode(document, model, node);
        }

        return model.FlowOf(command.EntryId) is { } flow
            ? SankeyWriter.Remove(document, flow.Range)
            : Gone;
    }
}
