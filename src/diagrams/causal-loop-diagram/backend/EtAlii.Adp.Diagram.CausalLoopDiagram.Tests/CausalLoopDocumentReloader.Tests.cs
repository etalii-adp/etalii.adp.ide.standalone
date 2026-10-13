using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>The shared reload seam over the causal loop store, held to the contract every module's is.</summary>
public sealed class CausalLoopDocumentReloaderTests : DocumentReloaderContract<ICausalLoopDocumentStore>
{
    protected override DiagramOrigin Origin => ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin;

    protected override string Example => CausalLoopDocumentStoreTests.OnCall;

    protected override ICausalLoopDocumentStore CreateStore() => new CausalLoopDocumentStore();

    protected override bool IsEmpty(ICausalLoopDocumentStore store, string path) => store.GetOrLoad(path).Model.Variables.Count == 0;

    protected override void OnChanged(ICausalLoopDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);
}
