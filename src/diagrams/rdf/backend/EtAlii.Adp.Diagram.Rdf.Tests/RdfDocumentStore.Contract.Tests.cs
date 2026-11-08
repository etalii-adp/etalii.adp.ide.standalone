using EtAlii.Adp.Files;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>The family's one store, held to the contract every module's is (code-structure FR-029).</summary>
public sealed class RdfDocumentStoreContractTests : DocumentStoreContract<IRdfDocumentStore, RdfBody, RdfModel>
{
    protected override string Example => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "constructs.ttl");

    protected override string EditMarker => "# store contract marker";

    protected override IRdfDocumentStore CreateStore() => new RdfDocumentStore(new FileWriter());

    protected override void Edit(RdfBody document) => document.Lines.Insert(0, [EditMarker]);

    protected override string TextOf(RdfBody document) => document.Text;

    protected override bool IsEmpty(RdfModel model) => model.Triples.Count == 0;
}
