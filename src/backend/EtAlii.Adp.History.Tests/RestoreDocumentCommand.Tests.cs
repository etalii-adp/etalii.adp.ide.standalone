using EtAlii.Adp.Documents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.History.Tests;

/// <summary>
/// The shared restore-a-file edit (backend-centralization R6.1, R6.2).
/// </summary>
public sealed class RestoreDocumentCommandTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-restore-" + Guid.NewGuid().ToString("N"));

    public RestoreDocumentCommandTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder a virus scanner still holds is not a test failure.
        }
    }

    [Fact]
    public async Task ARestore_WritesTheTextBack_ReloadsThroughTheStore_AndOffersTheRedo()
    {
        var path = IoPath.Combine(_folder, "body.yml");
        await File.WriteAllTextAsync(path, "after the edit", TestContext.Current.CancellationToken);
        var store = new FirstStore();
        var redo = new SampleEdit();

        var result = await new RestoreDocumentCommandHandler<FirstStore>(store)
            .ExecuteAsync(new RestoreDocumentCommand<FirstStore>(path, "before the edit\r\n", redo), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("before the edit\r\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal([path], store.Reloaded);
        Assert.Same(redo, result.Inverse);
    }

    [Fact]
    public async Task ARestoreOfAFileStillAsTheEditLeftIt_WritesTheTextBack()
    {
        var path = IoPath.Combine(_folder, "body.yml");
        await File.WriteAllTextAsync(path, "after the edit", TestContext.Current.CancellationToken);
        var store = new FirstStore();

        var result = await new RestoreDocumentCommandHandler<FirstStore>(store)
            .ExecuteAsync(new RestoreDocumentCommand<FirstStore>(path, "before the edit", new SampleEdit(), After: "after the edit"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("before the edit", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal([path], store.Reloaded);
    }

    [Fact]
    public async Task ARestoreOfAFileAnotherProgramChanged_WritesNothing_AndSaysTheRecordIsSpent()
    {
        // agent-activity-diagram R9.6. An agent wrote the file after the edit this would undo.
        // Putting the pre-edit text back would discard the agent's change without a word.
        var path = IoPath.Combine(_folder, "body.yml");
        await File.WriteAllTextAsync(path, "after the edit, and then an agent's line", TestContext.Current.CancellationToken);
        var store = new FirstStore();

        var result = await new RestoreDocumentCommandHandler<FirstStore>(store)
            .ExecuteAsync(new RestoreDocumentCommand<FirstStore>(path, "before the edit", new SampleEdit(), After: "after the edit"), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(path, result.OutdatedBodyPath);
        Assert.Contains("changed by another program", result.Error, StringComparison.Ordinal);
        Assert.Equal("after the edit, and then an agent's line", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(store.Reloaded);
    }

    [Fact]
    public async Task ARestoreThatCannotWrite_IsAFailedCommand_AndReloadsNothing()
    {
        // A path whose parent is a FILE cannot be written, whatever the writer tries.
        var blocker = IoPath.Combine(_folder, "not-a-folder");
        await File.WriteAllTextAsync(blocker, "", TestContext.Current.CancellationToken);
        var path = IoPath.Combine(blocker, "body.yml");
        var store = new FirstStore();

        var result = await new RestoreDocumentCommandHandler<FirstStore>(store)
            .ExecuteAsync(new RestoreDocumentCommand<FirstStore>(path, "text", new SampleEdit()), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("Could not restore the file", result.Error, StringComparison.Ordinal);
        Assert.Null(result.Inverse);

        // Reloading here would re-read a document that did not change, as though it had.
        Assert.Empty(store.Reloaded);
    }

    [Fact]
    public async Task TwoModulesRestores_EachReachTheirOwnStore_ThroughOneDispatcher()
    {
        // R6.2's point, and the reason the command is generic. The dispatcher finds a handler by
        // the command's type, so a single restore type registered by two modules would reach
        // whichever registered last and reload the other module's store. Registered in the
        // order that would make that mistake visible: the second module last.
        var first = new FirstStore();
        var second = new SecondStore();
        var services = new ServiceCollection()
            .AddSingleton(first)
            .AddSingleton(second)
            .AddSingleton<ICommandHandler<RestoreDocumentCommand<FirstStore>>, RestoreDocumentCommandHandler<FirstStore>>()
            .AddSingleton<ICommandHandler<RestoreDocumentCommand<SecondStore>>, RestoreDocumentCommandHandler<SecondStore>>()
            .BuildServiceProvider();
        var dispatcher = new CommandDispatcher(services);
        var path = IoPath.Combine(_folder, "first.yml");

        ICommand restore = new RestoreDocumentCommand<FirstStore>(path, "text", new SampleEdit());
        var result = await dispatcher.DispatchAsync(restore, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([path], first.Reloaded);
        Assert.Empty(second.Reloaded);
    }

    /// <summary>A module's store, as the restore sees it.</summary>
    private class RecordingStore : IReloadableDocumentStore
    {
        public List<string> Reloaded { get; } = [];

        public void Reload(string path) => Reloaded.Add(path);
    }

    private sealed class FirstStore : RecordingStore;

    private sealed class SecondStore : RecordingStore;

    private sealed record SampleEdit : ICommand;
}
