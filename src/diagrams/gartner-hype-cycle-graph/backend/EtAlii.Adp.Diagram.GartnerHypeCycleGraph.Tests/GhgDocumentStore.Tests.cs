namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>The store over <c>.ghg</c> bodies, held to the contract every module's is.</summary>
public sealed class GhgDocumentStoreTests : DocumentStoreContract<IGhgDocumentStore, GhgBody, GhgModel>
{
    protected override string Example => GhgModuleFiles.Example;

    protected override string EditMarker => "Store contract marker";

    protected override IGhgDocumentStore CreateStore() => new GhgDocumentStore();

    protected override void Edit(GhgBody document) =>
        GhgWriter.SetName(document, GhgParser.Parse(document).Trends[0], EditMarker);

    protected override string TextOf(GhgBody document) => document.Text;

    protected override bool IsEmpty(GhgModel model) => model.Trends.Count == 0 && model.Influences.Count == 0;
}
