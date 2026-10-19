using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>The shared reload seam over the causal loop store, held to the contract every module's is.</summary>
public sealed class CausalLoopDocumentReloaderTests : DocumentReloaderContract<ICausalLoopDocumentStore>
{
    protected override ToolOrigin Origin => ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin;

    protected override string Example => CausalLoopDocumentStoreTests.OnCall;

    protected override ICausalLoopDocumentStore CreateStore() => new CausalLoopDocumentStore(new FileWriter());

    protected override async Task<bool> IsEmptyAsync(ICausalLoopDocumentStore store, string path, CancellationToken cancellationToken) => (await store.GetOrLoadAsync(path, cancellationToken)).Model.Variables.Count == 0;

    protected override void OnChanged(ICausalLoopDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);
}
