using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>The causal loop command handlers, held to the contract every module's are, through the dispatcher the host builds.</summary>
public sealed class CausalLoopCommandHandlersTests : EditHandlerContract<ICausalLoopDocumentStore, CausalLoopBody, CausalLoopModel>
{
    protected override string Example => CausalLoopDocumentStoreTests.OnCall;

    protected override ICausalLoopDocumentStore CreateStore() => new CausalLoopDocumentStore(new FileWriter());

    protected override ICausalLoopDocumentStore Warning(ICausalLoopDocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(ICausalLoopDocumentStore store) =>
        new ServiceCollection()
            .AddSingleton(store)
            .AddCommands().AddHierarchyCommandHandlers().AddCausalLoop()
            .BuildServiceProvider()
            .GetRequiredService<ICommandDispatcher>();

    protected override ICommand EditOf(string bodyPath) => new SetVariableLabelCommand(bodyPath, "fatigue", "Tiredness");

    protected override ICommand EditOfMissing(string bodyPath) => new SetVariableLabelCommand(bodyPath, "no-such-variable", "Tiredness");

    [Fact]
    public async Task AVariableAddedAtAPoint_CarriesTheSaveWarning()
    {
        // Arrange: the drop writes the body and the registration, so its result is built apart from
        // the shared run's and the warning can be lost on the way.
        const string warning = "Something beside the document was not kept.";
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = TestFolder.Create("EtAlii.Adp.Diagram.CausalLoopDiagram.Tests");
        var body = folder.Combine(Path.GetFileName(Example));
        File.Copy(Example, body);
        var registration = folder.Combine("on-call.adp");
        await File.WriteAllTextAsync(registration, "systems/causal-loop-diagram\n", cancellationToken);
        var dispatcher = Dispatcher(Warning(CreateStore(), warning));

        // Act.
        var added = await dispatcher.DispatchAsync(new AddVariableAtCommand(body, registration, "morale", "Morale", 10, 20), cancellationToken);

        // Assert.
        Assert.True(added.IsSuccess, added.Error);
        Assert.IsType<RestoreVariableAdditionCommand>(added.Inverse);
        Assert.Equal(warning, added.Warning);
    }

    private sealed class WarningStore(ICausalLoopDocumentStore inner, string warning)
        : WarningDocumentStore<CausalLoopBody, CausalLoopModel>(inner, warning), ICausalLoopDocumentStore;
}
