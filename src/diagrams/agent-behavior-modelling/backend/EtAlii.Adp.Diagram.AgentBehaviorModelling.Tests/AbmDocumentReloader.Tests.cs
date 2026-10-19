using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>The shared reload seam over the behavior model store, held to the contract every module's is.</summary>
public sealed class AbmDocumentReloaderTests : DocumentReloaderContract<IAbmDocumentStore>
{
    protected override ToolOrigin Origin => Diagram.AgentBehaviorModelling.Origin;

    protected override string Example => AbmExamples.BodyOf("bug-fixer");

    protected override IAbmDocumentStore CreateStore() => new AbmDocumentStore(new FileWriter());

    protected override async Task<bool> IsEmptyAsync(IAbmDocumentStore store, string path, CancellationToken cancellationToken) => (await store.GetOrLoadAsync(path, cancellationToken)).Model.Nodes.Count == 0;

    protected override void OnChanged(IAbmDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);
}
