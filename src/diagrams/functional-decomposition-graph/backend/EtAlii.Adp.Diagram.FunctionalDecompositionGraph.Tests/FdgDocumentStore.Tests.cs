using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>The store over the <c>.fdg</c> store, held to the contract every module's is.</summary>
public sealed class FdgDocumentStoreTests : DocumentStoreContract<IFdgDocumentStore, LineDocument, FdgModel>
{
    protected override string Example => FieldServiceExample.Path;

    protected override string EditMarker => "# store contract marker";

    protected override IFdgDocumentStore CreateStore() => new FdgDocumentStore();

    protected override void Edit(LineDocument document) => document.Insert(1, [EditMarker]);

    protected override string TextOf(LineDocument document) => document.Text;

    protected override bool IsEmpty(FdgModel model) => model.Elements.Count == 0 && model.Connections.Count == 0;
}
