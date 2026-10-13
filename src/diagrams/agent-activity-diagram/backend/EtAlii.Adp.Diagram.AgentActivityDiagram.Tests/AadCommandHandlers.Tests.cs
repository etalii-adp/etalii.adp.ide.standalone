using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>What every handler does with a save, through the shared contract (code-structure FR-022).</summary>
public sealed class AadCommandHandlersTests : EditHandlerContract<IAadDocumentStore, AadBody, AadModel>
{
    protected override string Example => Path.Combine(AppContext.BaseDirectory, "Fixtures", "one-day.aad");

    protected override IAadDocumentStore CreateStore() => new AadDocumentStore();

    protected override IAadDocumentStore Warning(IAadDocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(IAadDocumentStore store) => new AttributeDispatcher(store);

    protected override ICommand EditOf(string bodyPath) => new SetAadAttributeCommand(bodyPath, "p-standalone", "name", "The standalone host");

    protected override ICommand EditOfMissing(string bodyPath) => new SetAadAttributeCommand(bodyPath, "p-gone", "name", "The standalone host");

    private sealed class WarningStore(IAadDocumentStore inner, string warning)
        : WarningEditSavingDocumentStore<AadBody, AadModel>(inner, warning), IAadDocumentStore;

    /// <summary>The one command the contract runs, routed to its handler.</summary>
    private sealed class AttributeDispatcher(IAadDocumentStore documents) : ICommandDispatcher
    {
        public Task<CommandResult> DispatchAsync(ICommand command, CancellationToken cancellationToken = default) =>
            new SetAadAttributeCommandHandler(documents).ExecuteAsync((SetAadAttributeCommand)command, cancellationToken);
    }
}
