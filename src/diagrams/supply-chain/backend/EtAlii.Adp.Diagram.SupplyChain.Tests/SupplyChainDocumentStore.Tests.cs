using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>The <c>.supply</c> store, held to the contract every module's store is.</summary>
public sealed class SupplyChainDocumentStoreTests : DocumentStoreContract<ISupplyChainDocumentStore, SupplyChainBody, SupplyChainModel>
{
    protected override string Example => SupplyChainExamples.Automotive;

    protected override string EditMarker => "Store contract marker";

    protected override ISupplyChainDocumentStore CreateStore() => new SupplyChainDocumentStore(new FileWriter());

    protected override void Edit(SupplyChainBody document) =>
        SupplyChainDefinition.Set(document, document.Reading.Model, document.Reading.Model.Nodes[0].Id, SupplyChainKeys.Name, EditMarker);

    protected override string TextOf(SupplyChainBody document) => document.Text;

    protected override bool IsEmpty(SupplyChainModel model) => model.Groups.Count == 0 && model.Nodes.Count == 0 && model.Flows.Count == 0;
}
