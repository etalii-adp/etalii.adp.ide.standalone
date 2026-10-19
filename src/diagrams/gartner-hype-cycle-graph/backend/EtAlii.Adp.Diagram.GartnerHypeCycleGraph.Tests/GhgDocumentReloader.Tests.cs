using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>The shared reload seam over the <c>.ghg</c> store, held to the contract every module's is.</summary>
public sealed class GhgDocumentReloaderTests : DocumentReloaderContract<IGhgDocumentStore>
{
    protected override ToolOrigin Origin => Diagram.HypeCycleGraph.Origin;

    protected override string Example => GhgModuleFiles.Example;

    protected override IGhgDocumentStore CreateStore() => new GhgDocumentStore(new FileWriter());

    protected override async Task<bool> IsEmptyAsync(IGhgDocumentStore store, string path, CancellationToken cancellationToken)
    {
        var model = (await store.GetOrLoadAsync(path, cancellationToken)).Model;
        return model.Trends.Count == 0 && model.Influences.Count == 0;
    }

    protected override void OnChanged(IGhgDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);
}
