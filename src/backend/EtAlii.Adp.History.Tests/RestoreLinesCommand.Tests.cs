using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.History.Tests;

/// <summary>The shared put-the-removed-lines-back edit (code-structure FR-033).</summary>
public sealed class RestoreLinesCommandTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "adp-restore-lines-" + Guid.NewGuid().ToString("N"));

    public RestoreLinesCommandTests() => Directory.CreateDirectory(_folder);

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
    public async Task ARestore_PutsTheLinesBackWhereTheyWere_ReloadsThroughTheStore_AndOffersTheRedo()
    {
        // Arrange: lines 1 and 3 of "a b c d e" were removed, captured ascending against the whole.
        var path = IoPath.Combine(_folder, "body.tml");
        await File.WriteAllTextAsync(path, "a\r\nc\r\ne\r\n", TestContext.Current.CancellationToken);
        var store = new RecordingStore();
        var redo = new SampleEdit();
        var segments = new[] { new LineSegment(1, ["b"]), new LineSegment(3, ["d"]) };

        // Act.
        var result = await new RestoreLinesCommandHandler<RecordingStore>(store, new FileWriter())
            .ExecuteAsync(new RestoreLinesCommand<RecordingStore>(path, segments, redo), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("a\r\nb\r\nc\r\nd\r\ne\r\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal([path], store.Reloaded);
        Assert.Same(redo, result.Inverse);
    }

    [Fact]
    public async Task ARestoreIntoAFileCutDownSince_IsRefused_WritesNothing_AndReloadsNothing()
    {
        // Arrange: the lines sat at index 5, and the file has two lines left.
        var path = IoPath.Combine(_folder, "body.tml");
        await File.WriteAllTextAsync(path, "a\r\nb\r\n", TestContext.Current.CancellationToken);
        var store = new RecordingStore();

        // Act.
        var result = await new RestoreLinesCommandHandler<RecordingStore>(store, new FileWriter())
            .ExecuteAsync(new RestoreLinesCommand<RecordingStore>(path, [new LineSegment(5, ["f"])], new SampleEdit()), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Equal("body.tml has changed too much since then to put that back.", result.Error);
        Assert.Equal("a\r\nb\r\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(store.Reloaded);
    }

    /// <summary>A module's store, as the restore sees it.</summary>
    private sealed class RecordingStore : IReloadableDocumentStore
    {
        public List<string> Reloaded { get; } = [];

        public Task ReloadAsync(string path, CancellationToken cancellationToken)
        {
            Reloaded.Add(path);
            return Task.CompletedTask;
        }
    }

    private sealed record SampleEdit : ICommand;
}
