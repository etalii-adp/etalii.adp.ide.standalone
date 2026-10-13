using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>What every handler does with a save, through the shared contract (code-structure FR-022).</summary>
public sealed class AbmCommandHandlersTests : EditHandlerContract<IAbmDocumentStore, AbmBody, AbmModel>
{
    protected override string Example => AbmExamples.BodyOf("bug-fixer");

    protected override IAbmDocumentStore CreateStore() => new AbmDocumentStore();

    protected override IAbmDocumentStore Warning(IAbmDocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(IAbmDocumentStore store) => new AbmTestDispatcher(store);

    protected override ICommand EditOf(string bodyPath) => new RenameAbmNodeCommand(bodyPath, "1.1", "Understand the bug report");

    protected override ICommand EditOfMissing(string bodyPath) => new RenameAbmNodeCommand(bodyPath, "9.9", "Understand the bug report");

    private sealed class WarningStore(IAbmDocumentStore inner, string warning)
        : WarningDocumentStore<AbmBody, AbmModel>(inner, warning), IAbmDocumentStore;
}
