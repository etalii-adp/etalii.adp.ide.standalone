using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>The store over the <c>.fdg</c> store, held to the contract every module's is.</summary>
public sealed class FdgDocumentStoreTests : DocumentStoreContract<IFdgDocumentStore, FdgBody, DislModel>
{
    protected override string Example => FieldServiceExample.Path;

    protected override string EditMarker => "# store contract marker";

    protected override IFdgDocumentStore CreateStore() => new FdgDocumentStore(new FileWriter());

    protected override void Edit(FdgBody document)
    {
        var lines = LineDocument.Parse(document.Text);
        lines.Insert(1, [EditMarker]);
        document.Rewrite(lines.Text);
    }

    protected override string TextOf(FdgBody document) => document.Text;

    protected override bool IsEmpty(DislModel model) => model.Diagram.Elements.Count == 0 && model.Diagram.Relations.Count == 0;
}
