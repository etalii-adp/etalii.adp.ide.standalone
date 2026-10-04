using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Removes whatever the id names.</summary>
public sealed class RemoveSankeyEntryCommandHandler(ISankeyDocumentStore documents) : ICommandHandler<RemoveSankeyEntryCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RemoveSankeyEntryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return SankeyEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (SankeyEdits.NodeOf(model, command.EntryId) is { } node)
            {
                return SankeyWriter.RemoveNode(document, model, node);
            }

            return SankeyEdits.FlowOf(model, command.EntryId) is { } flow
                ? SankeyWriter.Remove(document, flow.Range)
                : SankeyEdits.Gone();
        });
    }
}
