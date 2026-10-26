using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The <c>.supply</c> document store: the shared store, told how a body reads and what it means.
/// </summary>
/// <remarks>
/// A body is held as a <see cref="SupplyChainBody"/>, read through the binding and written by its changes; one that is
/// not YAML is held as unreadable with the YAML reader's sentence, so that nothing is written to it until it parses.
/// </remarks>
public sealed class SupplyChainDocumentStore(IFileWriter writer) : LifecycleDocumentStore<SupplyChainBody, SupplyChainModel>(writer), ISupplyChainDocumentStore
{
    /// <inheritdoc />
    protected override SupplyChainBody Read(string text) => SupplyChainBody.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(SupplyChainBody body) => body.Text;

    /// <inheritdoc />
    protected override ParseOutcome<SupplyChainModel> Parse(SupplyChainBody body) => body.Reading;
}
