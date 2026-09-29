using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>
/// The store's own save against the file watcher's reload of the same path. A watcher reports a
/// write while it is still being published, and every other writable store ignores a reload of a
/// path it is saving; this one does not, so a reload landing mid-publish can read the document
/// while it is missing or replaced and install what it read over the document in memory.
/// </summary>
public class CausalLoopDocumentStoreSelfWriteTests : IDisposable
{
    private const string Text =
        "causal-loop 1\r\n"
        + "variable a \"Alpha\"\r\n"
        + "variable b \"Beta\"\r\n"
        + "link a -> b +\r\n";

    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-causal-loop-store-" + Guid.NewGuid().ToString("N"));

    public CausalLoopDocumentStoreSelfWriteTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Reload_DuringItsOwnSaves_NeverReplacesTheDocumentWithWhatAPublishLeftBehind()
    {
        // Arrange.
        var path = IoPath.Combine(_workspace, "loop.cld");
        await File.WriteAllTextAsync(path, Text, TestContext.Current.CancellationToken);
        var store = new CausalLoopDocumentStore();
        _ = store.GetOrLoad(path);

        var saving = 1;
        var damaged = 0;
        var reloads = 0;
        string? first = null;
        var reloader = Task.Run(() =>
        {
            // ReSharper disable once AccessToModifiedClosure - Reason: Used in a test case which is acceptable.
            while (Volatile.Read(ref saving) == 1)
            {
                // What the reload bridge does when the watcher reports the path.
                store.Reload(path);
                reloads++;
                var entry = store.GetOrLoad(path);
                if (!entry.IsUsable || entry.Document.Text != Text)
                {
                    damaged++;
                    first ??= $"usable={entry.IsUsable}, length={entry.Document.Text.Length} of {Text.Length}, error=\"{entry.Error}\"";
                }
            }
        }, TestContext.Current.CancellationToken);

        // Act.
        var refused = 0;
        for (var i = 0; i < 2000; i++)
        {
            if (store.Save(path, store.GetOrLoad(path)).Failed)
            {
                refused++;
            }
        }

        Volatile.Write(ref saving, 0);
        await reloader;

        // Assert.
        Assert.True(reloads > 0, "The reloader never ran, so this run proves nothing.");
        Assert.True(damaged == 0 && refused == 0, $"{damaged} of {reloads} reloads left a damaged document; {refused} of 2000 saves were refused. First damage: {first}");
    }
}
