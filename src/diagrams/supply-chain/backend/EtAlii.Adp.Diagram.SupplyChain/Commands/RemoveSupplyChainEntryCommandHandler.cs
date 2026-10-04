using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Removes whatever the id names.</summary>
public sealed class RemoveSupplyChainEntryCommandHandler(ISupplyChainDocumentStore documents) : ICommandHandler<RemoveSupplyChainEntryCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RemoveSupplyChainEntryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return SupplyChainEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (SupplyChainEdits.NodeOf(model, command.EntryId) is { } node)
            {
                return SupplyChainWriter.RemoveNode(document, model, node);
            }

            if (SupplyChainEdits.FlowOf(model, command.EntryId) is { } flow)
            {
                return SupplyChainWriter.Remove(document, flow.Range);
            }

            return SupplyChainEdits.GroupOf(model, command.EntryId) is { } group
                ? SupplyChainWriter.RemoveGroup(document, model, group)
                : SupplyChainEdits.Gone();
        });
    }
}
