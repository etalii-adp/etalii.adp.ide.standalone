using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>The shared reload seam over the <c>.fdg</c> store, held to the contract every module's is.</summary>
public sealed class FdgDocumentReloaderTests : DocumentReloaderContract<IFdgDocumentStore>
{
    protected override DiagramOrigin Origin => Diagram.FunctionalDecompositionGraph.Origin;

    protected override string Example => FieldServiceExample.Path;

    protected override IFdgDocumentStore CreateStore() => new FdgDocumentStore();

    protected override bool IsEmpty(IFdgDocumentStore store, string path)
    {
        var model = store.GetOrLoad(path).Model;
        return model.Elements.Count == 0 && model.Connections.Count == 0;
    }

    protected override void OnChanged(IFdgDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);
}
