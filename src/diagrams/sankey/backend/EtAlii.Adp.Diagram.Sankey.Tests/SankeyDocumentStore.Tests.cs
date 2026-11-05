using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>The <c>.skv</c> store, held to the contract every module's store is.</summary>
public sealed class SankeyDocumentStoreTests : DocumentStoreContract<ISankeyDocumentStore, SankeyBody, SankeyModel>
{
    protected override string Example => SankeyExamples.BrightwaterCoffee;

    protected override string EditMarker => "Store contract marker";

    protected override ISankeyDocumentStore CreateStore() => new SankeyDocumentStore(new FileWriter());

    protected override void Edit(SankeyBody document) =>
        SankeyEdits.SetText(document, document.Reading.Model.Nodes[0], SankeyKeys.Name, EditMarker, removeWhenEmpty: false);

    protected override string TextOf(SankeyBody document) => document.Text;

    protected override bool IsEmpty(SankeyModel model) => model.Nodes.Count == 0 && model.Flows.Count == 0;
}
