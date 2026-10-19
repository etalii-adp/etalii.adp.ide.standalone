using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>The Wardley command handlers, held to the contract every module's are, through the dispatcher the host builds.</summary>
public sealed class WardleyCommandHandlersTests : EditHandlerContract<IWardleyDocumentStore, LineDocument, WardleyMap>
{
    protected override string Example => WardleyDocumentStoreTests.Minimal;

    protected override IWardleyDocumentStore CreateStore() => new WardleyDocumentStore(new FileWriter());

    protected override IWardleyDocumentStore Warning(IWardleyDocumentStore store, string warning) => new WarningStore(store, warning);

    protected override ICommandDispatcher Dispatcher(IWardleyDocumentStore store) =>
        new ServiceCollection()
            .AddSingleton(store)
            .AddCommands().AddHierarchyCommandHandlers().AddWardleyMap()
            .BuildServiceProvider()
            .GetRequiredService<ICommandDispatcher>();

    // The ids of a map are minted when it is first read, so the edit that needs none is an add.
    protected override ICommand EditOf(string bodyPath) => new AddWardleyElementCommand(bodyPath, "component", "Kettle", 0.4d, 0.3d);

    protected override ICommand EditOfMissing(string bodyPath) => new RenameWardleyElementCommand(bodyPath, "no-such-element", "Kettle");

    [Fact]
    public async Task AMoveAndItsLineRestore_CarryTheSaveWarning()
    {
        // Arrange: a move names its own undo, a restore of the one line it changed, so its result
        // is built apart from the shared run's and the warning can be lost on the way.
        const string warning = "Something beside the document was not kept.";
        using var folder = TestFolder.Create("EtAlii.Adp.Diagram.WardleyMap.Tests");
        var body = folder.Combine(Path.GetFileName(Example));
        File.Copy(Example, body);
        var store = Warning(CreateStore(), warning);
        var dispatcher = Dispatcher(store);
        await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken);
        var cup = store.Identities(body).Single(entry => entry.Key == "Cup of Tea").Id;

        // Act.
        var moved = await dispatcher.DispatchAsync(new MoveWardleyElementCommand(body, cup, 0.5d, 0.5d), TestContext.Current.CancellationToken);
        var restored = await dispatcher.DispatchAsync(moved.Inverse!, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(moved.IsSuccess, moved.Error);
        Assert.Equal(warning, moved.Warning);
        Assert.IsType<RestoreWardleyLineCommand>(moved.Inverse);
        Assert.True(restored.IsSuccess, restored.Error);
        Assert.Equal(warning, restored.Warning);
        Assert.Equal(await File.ReadAllTextAsync(Example, TestContext.Current.CancellationToken), await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
    }

    private sealed class WarningStore(IWardleyDocumentStore inner, string warning)
        : WarningDocumentStore<LineDocument, WardleyMap>(inner, warning), IWardleyDocumentStore
    {
        public IReadOnlyList<WardleyIdentityEntry> Identities(string path) => inner.Identities(path);

        public void Rekey(string path, string kind, string oldKey, string newKey) => inner.Rekey(path, kind, oldKey, newKey);
    }
}
