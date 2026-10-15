using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// The <c>.supply</c> document store: the shared store, told how a body reads and what it means.
/// </summary>
public sealed class SupplyChainDocumentStore : LifecycleDocumentStore<LineDocument, SupplyChainModel>, ISupplyChainDocumentStore
{
    /// <inheritdoc />
    protected override LineDocument Read(string text) => LineDocument.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(LineDocument body) => body.Text;

    /// <inheritdoc />
    protected override SupplyChainModel Rebuild(LineDocument body) => SupplyChainParser.Parse(body);

    /// <inheritdoc />
    protected override ParseOutcome<SupplyChainModel> Parse(LineDocument body) => SupplyChainParser.Outcome(body);
}
