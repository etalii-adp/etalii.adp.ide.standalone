using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>The shared reload seam over the <c>.ghg</c> store, held to the contract every module's is.</summary>
public sealed class GhgDocumentReloaderTests : DocumentReloaderContract<IGhgDocumentStore>
{
    protected override DiagramOrigin Origin => Diagram.HypeCycleGraph.Origin;

    protected override string Example => GhgModuleFiles.Example;

    protected override IGhgDocumentStore CreateStore() => new GhgDocumentStore();

    protected override bool IsEmpty(IGhgDocumentStore store, string path)
    {
        var model = store.GetOrLoad(path).Model;
        return model.Trends.Count == 0 && model.Influences.Count == 0;
    }

    protected override void OnChanged(IGhgDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);
}
