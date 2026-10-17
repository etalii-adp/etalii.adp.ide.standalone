namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>The shared reload seam over the <c>.skv</c> store, held to the contract every module's is.</summary>
public sealed class SankeyDocumentReloaderTests : DocumentReloaderContract<ISankeyDocumentStore>
{
    protected override ToolOrigin Origin => Diagram.Sankey.Origin;

    protected override string Example => SankeyExamples.BrightwaterCoffee;

    protected override ISankeyDocumentStore CreateStore() => new SankeyDocumentStore();

    protected override bool IsEmpty(ISankeyDocumentStore store, string path)
    {
        var model = store.GetOrLoad(path).Model;
        return model.Nodes.Count == 0 && model.Flows.Count == 0;
    }

    protected override void OnChanged(ISankeyDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);
}
