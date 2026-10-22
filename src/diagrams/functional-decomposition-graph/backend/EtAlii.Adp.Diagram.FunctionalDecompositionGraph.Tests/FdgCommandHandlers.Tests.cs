using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>What every handler does with a save, through the shared contract (code-structure FR-022).</summary>
public sealed class FdgCommandHandlersTests : EditHandlerContract<IFdgDocumentStore, FdgBody, DislModel>
{
    protected override string Example => FieldServiceExample.Path;

    protected override IFdgDocumentStore CreateStore() => new FdgDocumentStore(new FileWriter());

    protected override IFdgDocumentStore Warning(IFdgDocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(IFdgDocumentStore store) => new FdgTestDispatcher(store);

    protected override ICommand EditOf(string bodyPath) => new RenameFdgElementCommand(bodyPath, "planning", "Dispatch board");

    protected override ICommand EditOfMissing(string bodyPath) => new RenameFdgElementCommand(bodyPath, "no-such-element", "Dispatch board");

    private sealed class WarningStore(IFdgDocumentStore inner, string warning)
        : WarningDocumentStore<FdgBody, DislModel>(inner, warning), IFdgDocumentStore;
}
