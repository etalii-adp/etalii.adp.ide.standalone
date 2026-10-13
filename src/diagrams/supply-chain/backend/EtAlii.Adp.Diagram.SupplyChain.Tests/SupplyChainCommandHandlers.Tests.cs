using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>What every handler does with a save, through the shared contract (code-structure FR-022).</summary>
public sealed class SupplyChainCommandHandlersTests : EditHandlerContract<ISupplyChainDocumentStore, LineDocument, SupplyChainModel>
{
    protected override string Example => SupplyChainExamples.Automotive;

    protected override ISupplyChainDocumentStore CreateStore() => new SupplyChainDocumentStore();

    protected override ISupplyChainDocumentStore Warning(ISupplyChainDocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(ISupplyChainDocumentStore store) => new SupplyChainTestDispatcher(store);

    protected override ICommand EditOf(string bodyPath) => new SetSupplyChainPropertyCommand(bodyPath, "south-america", SupplyChainKeys.Name, "Andes");

    protected override ICommand EditOfMissing(string bodyPath) => new SetSupplyChainPropertyCommand(bodyPath, "no-such-entry", SupplyChainKeys.Name, "Andes");

    private sealed class WarningStore(ISupplyChainDocumentStore inner, string warning)
        : WarningDocumentStore<LineDocument, SupplyChainModel>(inner, warning), ISupplyChainDocumentStore;
}
