using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The control for the causal-loop store's self-write probe: the same saves and the same
/// concurrent reloads, against a store that ignores a reload of a path it is saving.
/// </summary>
public class TimelineDocumentStoreSelfWriteTests : IDisposable
{
    private const string Text = "timeline: 1\nelements:\n  - id: aaa\n    label: Period\n    begin: 2026-01-05\n    end: 2026-02-13\n    row: 0\n";

    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-timeline-store-" + Guid.NewGuid().ToString("N"));

    public TimelineDocumentStoreSelfWriteTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Reload_DuringItsOwnSaves_NeverReplacesTheDocumentWithWhatAPublishLeftBehind()
    {
        // Arrange.
        var path = IoPath.Combine(_workspace, "plan.tml");
        File.WriteAllText(path, Text);
        var store = new TimelineDocumentStore();
        _ = store.GetOrLoad(path);

        var saving = 1;
        var damaged = 0;
        var reloads = 0;
        var reloader = Task.Run(() =>
        {
            while (Volatile.Read(ref saving) == 1)
            {
                store.Reload(path);
                reloads++;
                var entry = store.GetOrLoad(path);
                if (!entry.IsUsable || entry.Document.Text != Text)
                {
                    damaged++;
                }
            }
        }, TestContext.Current.CancellationToken);

        // Act.
        var refused = 0;
        for (var i = 0; i < 2000; i++)
        {
            if (store.Save(path).Length > 0)
            {
                refused++;
            }
        }

        Volatile.Write(ref saving, 0);
        await reloader;

        // Assert.
        Assert.True(reloads > 0, "The reloader never ran, so this run proves nothing.");
        Assert.True(damaged == 0 && refused == 0, $"{damaged} of {reloads} reloads left a damaged document; {refused} of 2000 saves were refused.");
    }
}
