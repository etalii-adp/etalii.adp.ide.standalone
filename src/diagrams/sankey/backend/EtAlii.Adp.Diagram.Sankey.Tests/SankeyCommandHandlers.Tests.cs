using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>What every handler does with a save, through the shared contract (code-structure FR-022).</summary>
public sealed class SankeyCommandHandlersTests : EditHandlerContract<ISankeyDocumentStore, SankeyBody, SankeyModel>
{
    protected override string Example => SankeyExamples.BrightwaterCoffee;

    protected override ISankeyDocumentStore CreateStore() => new SankeyDocumentStore(new FileWriter());

    protected override ISankeyDocumentStore Warning(ISankeyDocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(ISankeyDocumentStore store) => TestHistory.Dispatcher(s => { s.AddSankey(); s.AddSingleton(store); });

    protected override ICommand EditOf(string bodyPath) => new SetSankeyPropertyCommand(bodyPath, "cafes", SankeyKeys.Name, "Coffee bars");

    protected override ICommand EditOfMissing(string bodyPath) => new SetSankeyPropertyCommand(bodyPath, "no-such-node", SankeyKeys.Name, "Coffee bars");

    private sealed class WarningStore(ISankeyDocumentStore inner, string warning)
        : WarningDocumentStore<SankeyBody, SankeyModel>(inner, warning), ISankeyDocumentStore;
}
