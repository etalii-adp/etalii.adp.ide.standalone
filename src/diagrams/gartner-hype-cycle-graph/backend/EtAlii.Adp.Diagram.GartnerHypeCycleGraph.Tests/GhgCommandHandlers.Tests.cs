using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>What every handler does with a save, through the shared contract (code-structure FR-022).</summary>
public sealed class GhgCommandHandlersTests : EditHandlerContract<IGhgDocumentStore, GhgBody, GhgModel>
{
    protected override string Example => GhgModuleFiles.Example;

    protected override IGhgDocumentStore CreateStore() => new GhgDocumentStore();

    protected override IGhgDocumentStore Warning(IGhgDocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(IGhgDocumentStore store) => new GhgTestDispatcher(store);

    protected override ICommand EditOf(string bodyPath) => new RenameGhgElementCommand(bodyPath, "industrial-revolution", "First industrial revolution");

    protected override ICommand EditOfMissing(string bodyPath) => new RenameGhgElementCommand(bodyPath, "no-such-trend", "First industrial revolution");

    private sealed class WarningStore(IGhgDocumentStore inner, string warning)
        : WarningDocumentStore<GhgBody, GhgModel>(inner, warning), IGhgDocumentStore;
}
