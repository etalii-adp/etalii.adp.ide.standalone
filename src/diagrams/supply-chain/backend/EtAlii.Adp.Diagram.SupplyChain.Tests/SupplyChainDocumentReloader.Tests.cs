using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>The shared reload seam over the <c>.supply</c> store, held to the contract every module's is.</summary>
public sealed class SupplyChainDocumentReloaderTests : DocumentReloaderContract<ISupplyChainDocumentStore>
{
    protected override ToolOrigin Origin => Diagram.SupplyChain.Origin;

    protected override string Example => SupplyChainExamples.Automotive;

    protected override ISupplyChainDocumentStore CreateStore() => new SupplyChainDocumentStore(new FileWriter());

    protected override async Task<bool> IsEmptyAsync(ISupplyChainDocumentStore store, string path, CancellationToken cancellationToken)
    {
        var model = (await store.GetOrLoadAsync(path, cancellationToken)).Model;
        return model.Groups.Count == 0 && model.Nodes.Count == 0 && model.Flows.Count == 0;
    }

    protected override void OnChanged(ISupplyChainDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);
}
