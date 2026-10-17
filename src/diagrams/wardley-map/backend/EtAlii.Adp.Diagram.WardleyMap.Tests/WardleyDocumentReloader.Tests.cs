namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>The shared reload seam over the Wardley map store, held to the contract every module's is.</summary>
public sealed class WardleyDocumentReloaderTests : DocumentReloaderContract<IWardleyDocumentStore>
{
    protected override ToolOrigin Origin => Diagram.WardleyMap.Origin;

    protected override string Example => WardleyDocumentStoreTests.Minimal;

    protected override IWardleyDocumentStore CreateStore() => new WardleyDocumentStore();

    protected override bool IsEmpty(IWardleyDocumentStore store, string path) => store.GetOrLoad(path).Model.Components.Count == 0;

    protected override void OnChanged(IWardleyDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);
}
