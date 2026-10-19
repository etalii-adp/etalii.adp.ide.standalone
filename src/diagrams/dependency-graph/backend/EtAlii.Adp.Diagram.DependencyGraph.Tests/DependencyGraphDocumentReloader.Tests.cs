using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>The shared reload seam over the dependency graph store, held to the contract every module's is.</summary>
public sealed class DependencyGraphDocumentReloaderTests : DocumentReloaderContract<IDependencyGraphDocumentStore>
{
    protected override ToolOrigin Origin => Diagram.DependencyGraph.Origin;

    protected override string Example => DependencyGraphDocumentStoreTests.Simple;

    protected override IDependencyGraphDocumentStore CreateStore() => new DependencyGraphDocumentStore(new FileWriter());

    protected override async Task<bool> IsEmptyAsync(IDependencyGraphDocumentStore store, string path, CancellationToken cancellationToken) => (await store.GetOrLoadAsync(path, cancellationToken)).Model.Elements.Count == 0;

    protected override void OnChanged(IDependencyGraphDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);
}
