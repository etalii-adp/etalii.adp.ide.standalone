using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>The C4 command handlers, held to the contract every module's are, through the dispatcher the host builds.</summary>
public sealed class C4CommandHandlersTests : EditHandlerContract<IC4DocumentStore, LineDocument, C4Workspace>
{
    protected override string Example => C4DocumentStoreTests.Courier;

    protected override IC4DocumentStore CreateStore() => new C4DocumentStore();

    protected override IC4DocumentStore Warning(IC4DocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(IC4DocumentStore store) =>
        new ServiceCollection()
            .AddSingleton(store)
            .AddCommands().AddHierarchyCommandHandlers().AddC4()
            .BuildServiceProvider()
            .GetRequiredService<ICommandDispatcher>();

    protected override ICommand EditOf(string bodyPath) => new SetElementNameCommand(bodyPath, "tracking", "Parcel Tracking");

    protected override ICommand EditOfMissing(string bodyPath) => new SetElementNameCommand(bodyPath, "no-such-element", "Parcel Tracking");

    [Fact]
    public async Task AddingAndRemovingAView_CarryTheSaveWarning_AndKeepTheRegistrationInStep()
    {
        // Arrange: the view handlers write and delete a registration after the edit run, so their
        // result is built twice and the warning can be lost on the way.
        const string warning = "Something beside the document was not kept.";
        using var folder = TestFolder.Create("EtAlii.Adp.Diagram.C4.Tests");
        var body = folder.Combine(Path.GetFileName(Example));
        File.Copy(Example, body);
        var registration = folder.Combine("landscape.adp");
        var dispatcher = Dispatcher(Warning(CreateStore(), warning));

        // Act.
        var added = await dispatcher.DispatchAsync(
            new AddC4ViewCommand(body, registration, "c4/system-landscape", C4ViewKind.SystemLandscape, "added-landscape", Path.GetFileName(body)),
            TestContext.Current.CancellationToken);
        var registered = File.Exists(registration);
        var removed = await dispatcher.DispatchAsync(added.Inverse!, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(added.IsSuccess, added.Error);
        Assert.Equal(warning, added.Warning);
        Assert.True(registered);
        Assert.True(removed.IsSuccess, removed.Error);
        Assert.Equal(warning, removed.Warning);
        Assert.False(File.Exists(registration));
    }

    private sealed class WarningStore(IC4DocumentStore inner, string warning)
        : WarningDocumentStore<LineDocument, C4Workspace>(inner, warning), IC4DocumentStore
    {
        public void Touch(string path) => inner.Touch(path);
    }
}
