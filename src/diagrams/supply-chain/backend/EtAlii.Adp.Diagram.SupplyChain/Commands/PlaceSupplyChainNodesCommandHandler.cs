using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Writes the placements, in one edit and so in one undo.</summary>
public sealed class PlaceSupplyChainNodesCommandHandler(ISupplyChainDocumentStore documents) : ICommandHandler<PlaceSupplyChainNodesCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(PlaceSupplyChainNodesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return SupplyChainEdits.Run(documents, command.BodyPath, command, (document, model) =>
            SupplyChainWriter.Place(document, model, command.Places));
    }
}
