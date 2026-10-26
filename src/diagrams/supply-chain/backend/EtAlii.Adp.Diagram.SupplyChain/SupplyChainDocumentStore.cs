using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The <c>.supply</c> document store: the shared store, told how a body reads and what it means.
/// </summary>
public sealed class SupplyChainDocumentStore(IFileWriter writer) : LifecycleDocumentStore<LineDocument, SupplyChainModel>(writer), ISupplyChainDocumentStore
{
    /// <inheritdoc />
    protected override LineDocument Read(string text) => LineDocument.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(LineDocument body) => body.Text;


    /// <inheritdoc />
    protected override ParseOutcome<SupplyChainModel> Parse(LineDocument body) => SupplyChainReading.Outcome(body.Text);
}
