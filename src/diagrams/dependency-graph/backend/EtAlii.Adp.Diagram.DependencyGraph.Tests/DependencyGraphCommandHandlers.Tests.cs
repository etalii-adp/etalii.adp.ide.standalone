using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>What every handler does with a save, through the shared contract (code-structure FR-022).</summary>
public sealed class DependencyGraphCommandHandlersTests : EditHandlerContract<IDependencyGraphDocumentStore, DependencyGraphBody, DislModel>
{
    protected override string Example => DependencyGraphDocumentStoreTests.Simple;

    protected override IDependencyGraphDocumentStore CreateStore() => new DependencyGraphDocumentStore(new FileWriter());

    protected override IDependencyGraphDocumentStore Warning(IDependencyGraphDocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(IDependencyGraphDocumentStore store) => new DependencyGraphTestDispatcher(store);

    protected override ICommand EditOf(string bodyPath) => new RenameDependencyGraphElementCommand(bodyPath, "k7Qv2mXa", "Edge proxy");

    protected override ICommand EditOfMissing(string bodyPath) => new RenameDependencyGraphElementCommand(bodyPath, "gone0000", "Edge proxy");

    private sealed class WarningStore(IDependencyGraphDocumentStore inner, string warning)
        : WarningDocumentStore<DependencyGraphBody, DislModel>(inner, warning), IDependencyGraphDocumentStore;
}
