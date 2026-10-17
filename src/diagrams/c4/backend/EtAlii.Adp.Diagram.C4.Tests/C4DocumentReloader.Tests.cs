namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>The shared reload seam over the C4 store, held to the contract every module's is.</summary>
public sealed class C4DocumentReloaderTests : DocumentReloaderContract<IC4DocumentStore>
{
    protected override ToolOrigin Origin => new("c4", "context");

    protected override string Example => C4DocumentStoreTests.Courier;

    protected override IC4DocumentStore CreateStore() => new C4DocumentStore();

    protected override bool IsEmpty(IC4DocumentStore store, string path) => store.GetOrLoad(path).Model.Elements.Count == 0;

    protected override void OnChanged(IC4DocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);
}
