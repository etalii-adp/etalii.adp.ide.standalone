using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>What every handler does with a save, through the shared contract (code-structure FR-022).</summary>
public sealed class SupplyChainCommandHandlersTests : EditHandlerContract<ISupplyChainDocumentStore, SupplyChainBody, SupplyChainModel>
{
    protected override string Example => SupplyChainExamples.Automotive;

    protected override ISupplyChainDocumentStore CreateStore() => new SupplyChainDocumentStore(new FileWriter());

    protected override ISupplyChainDocumentStore Warning(ISupplyChainDocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(ISupplyChainDocumentStore store) => TestHistory.Dispatcher(s => { s.AddSupplyChain(); s.AddSingleton(store); });

    protected override ICommand EditOf(string bodyPath) => new SetSupplyChainPropertyCommand(bodyPath, "south-america", SupplyChainKeys.Name, "Andes");

    protected override ICommand EditOfMissing(string bodyPath) => new SetSupplyChainPropertyCommand(bodyPath, "no-such-entry", SupplyChainKeys.Name, "Andes");

    private sealed class WarningStore(ISupplyChainDocumentStore inner, string warning)
        : WarningDocumentStore<SupplyChainBody, SupplyChainModel>(inner, warning), ISupplyChainDocumentStore;
}
