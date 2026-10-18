using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>The <c>.supply</c> store, held to the contract every module's store is.</summary>
public sealed class SupplyChainDocumentStoreTests : DocumentStoreContract<ISupplyChainDocumentStore, LineDocument, SupplyChainModel>
{
    protected override string Example => SupplyChainExamples.Automotive;

    protected override string EditMarker => "# store contract marker";

    protected override ISupplyChainDocumentStore CreateStore() => new SupplyChainDocumentStore(new FileWriter());

    protected override void Edit(LineDocument document) => document.Insert(1, [EditMarker]);

    protected override string TextOf(LineDocument document) => document.Text;

    protected override bool IsEmpty(SupplyChainModel model) => model.Groups.Count == 0 && model.Nodes.Count == 0 && model.Flows.Count == 0;
}
