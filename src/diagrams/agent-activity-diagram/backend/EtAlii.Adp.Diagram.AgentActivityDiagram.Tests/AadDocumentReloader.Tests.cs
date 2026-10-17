namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>The shared reload seam over the activity file store, held to the contract every module's is.</summary>
public sealed class AadDocumentReloaderTests : DocumentReloaderContract<IAadDocumentStore>
{
    protected override ToolOrigin Origin => Diagram.AgentActivity.Origin;

    protected override string Example => AadDocumentStoreTests.OneDay;

    protected override IAadDocumentStore CreateStore() => new AadDocumentStore();

    protected override bool IsEmpty(IAadDocumentStore store, string path) => store.GetOrLoad(path).Model.Elements.Count == 0;

    protected override void OnChanged(IAadDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);
}
