using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// Writes the layered layout into the document: the positions <see cref="SupplyChainLayout"/>
/// computes for every node, whatever the document stated before.
/// </summary>
/// <remarks>
/// Written rather than cleared: a document whose nodes carry no coordinates is already drawn by
/// this layout, so arranging it writes them all, and from then on dragging one moves only that one.
/// </remarks>
public sealed class ArrangeSupplyChainCommandHandler(ISupplyChainDocumentStore documents) : ICommandHandler<ArrangeSupplyChainCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(ArrangeSupplyChainCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return SupplyChainEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            var arranged = SupplyChainLayout.Of(model).Arranged;
            return arranged.Count == 0
                ? SupplyChainEdit.Refused("There is nothing to arrange until this diagram has a node.")
                : SupplyChainWriter.Place(document, model, arranged);
        });
    }
}
