using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The definition's <c>arrange</c>: its <c>chain</c> layout, which is the host's to run, written into the document as
/// the positions <see cref="SupplyChainLayout"/> computes for every node, whatever the document stated before.
/// </summary>
/// <remarks>
/// Written rather than cleared: a document whose nodes carry no coordinates is already drawn by
/// this layout, so arranging it writes them all, and from then on dragging one moves only that one.
/// </remarks>
public sealed class ArrangeSupplyChainCommandHandler(ISupplyChainDocumentStore documents) : SupplyChainEditHandler<ArrangeSupplyChainCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(SupplyChainBody document, SupplyChainModel model, ArrangeSupplyChainCommand command) =>
        SupplyChainDefinition.Arrange(document, model);
}
