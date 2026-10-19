using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>The shared reload seam over the <c>.fdg</c> store, held to the contract every module's is.</summary>
public sealed class FdgDocumentReloaderTests : DocumentReloaderContract<IFdgDocumentStore>
{
    protected override ToolOrigin Origin => Diagram.FunctionalDecompositionGraph.Origin;

    protected override string Example => FieldServiceExample.Path;

    protected override IFdgDocumentStore CreateStore() => new FdgDocumentStore(new FileWriter());

    protected override async Task<bool> IsEmptyAsync(IFdgDocumentStore store, string path, CancellationToken cancellationToken)
    {
        var model = (await store.GetOrLoadAsync(path, cancellationToken)).Model;
        return model.Elements.Count == 0 && model.Connections.Count == 0;
    }

    protected override void OnChanged(IFdgDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);
}
