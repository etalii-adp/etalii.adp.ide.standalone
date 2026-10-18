using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>The <c>.skv</c> store, held to the contract every module's store is.</summary>
public sealed class SankeyDocumentStoreTests : DocumentStoreContract<ISankeyDocumentStore, LineDocument, SankeyModel>
{
    protected override string Example => SankeyExamples.BrightwaterCoffee;

    protected override string EditMarker => "# store contract marker";

    protected override ISankeyDocumentStore CreateStore() => new SankeyDocumentStore(new FileWriter());

    protected override void Edit(LineDocument document) => document.Insert(1, [EditMarker]);

    protected override string TextOf(LineDocument document) => document.Text;

    protected override bool IsEmpty(SankeyModel model) => model.Nodes.Count == 0 && model.Flows.Count == 0;
}
