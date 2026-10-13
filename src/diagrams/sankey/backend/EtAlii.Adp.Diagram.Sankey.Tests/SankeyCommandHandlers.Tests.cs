using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>What every handler does with a save, through the shared contract (code-structure FR-022).</summary>
public sealed class SankeyCommandHandlersTests : EditHandlerContract<ISankeyDocumentStore, LineDocument, SankeyModel>
{
    protected override string Example => SankeyExamples.BrightwaterCoffee;

    protected override ISankeyDocumentStore CreateStore() => new SankeyDocumentStore();

    protected override ISankeyDocumentStore Warning(ISankeyDocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(ISankeyDocumentStore store) => new SankeyTestDispatcher(store);

    protected override ICommand EditOf(string bodyPath) => new SetSankeyPropertyCommand(bodyPath, "cafes", SankeyKeys.Name, "Coffee bars");

    protected override ICommand EditOfMissing(string bodyPath) => new SetSankeyPropertyCommand(bodyPath, "no-such-node", SankeyKeys.Name, "Coffee bars");

    private sealed class WarningStore(ISankeyDocumentStore inner, string warning)
        : WarningDocumentStore<LineDocument, SankeyModel>(inner, warning), ISankeyDocumentStore;
}
