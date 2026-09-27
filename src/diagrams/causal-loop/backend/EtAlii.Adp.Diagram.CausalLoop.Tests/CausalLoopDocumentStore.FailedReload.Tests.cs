using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// A reload that cannot read the body must not leave the store holding something a later save
/// would write over the real file.
/// </summary>
/// <remarks>
/// A failed read installed an Unreadable entry whose document is empty, and <c>Save</c> writes the
/// entry's document without asking whether it is usable - so the next save after a failed reload
/// could replace a real diagram with an empty file. The read failure is made deterministic here
/// with a handle that shares nothing, instead of racing a publish for it.
/// </remarks>
public class CausalLoopDocumentStoreFailedReloadTests : IDisposable
{
    private const string Text =
        "causal-loop 1\r\n"
        + "variable a \"Alpha\"\r\n"
        + "variable b \"Beta\"\r\n"
        + "link a -> b +\r\n";

    private readonly string _workspace = IoPath.Combine(IoPath.GetTempPath(), "adp-causal-loop-failed-reload-" + Guid.NewGuid().ToString("N"));

    public CausalLoopDocumentStoreFailedReloadTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ASaveAfterAReloadThatCouldNotRead_DoesNotWriteAnEmptyDocumentOverTheRealFile()
    {
        // Arrange: a real diagram, loaded.
        var path = IoPath.Combine(_workspace, "loop.cld");
        File.WriteAllText(path, Text);
        var store = new CausalLoopDocumentStore();
        Assert.True(store.GetOrLoad(path).IsUsable);

        // Arrange, continued: a reload that cannot read the body.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store.Reload(path);
        }

        // Assert, first: the store still holds the diagram - the failed read did not replace it.
        // This is what keeps the canvas drawn, and it is asserted on its own because the save
        // refusal below would protect the file even without it.
        var held = store.GetOrLoad(path);
        Assert.True(held.IsUsable, $"The failed reload replaced the last good diagram: {held.Error}");

        // Act: whatever triggers the next save, once the file is readable again.
        store.Save(path, held);

        // Assert: the real diagram is still on disk.
        Assert.Equal(Text, File.ReadAllText(path));
    }

    [Fact]
    public void ABodyThatIsReallyDeleted_IsNoLongerDrawn_NotTheLastDiagramKeptAlive()
    {
        // The other half of keeping the last good diagram. DiagramDocumentReloadBridge documents
        // that "a body that is gone is an empty diagram, not the last one kept alive". A missing
        // body is confirmed gone before it is treated as gone; this one never comes back, so the
        // store must stop holding the diagram that was deleted. Gone is the watcher's Deleted event,
        // which a publish in flight never raises.
        var path = IoPath.Combine(_workspace, "loop.cld");
        File.WriteAllText(path, Text);
        var store = new CausalLoopDocumentStore();
        Assert.True(store.GetOrLoad(path).IsUsable);

        File.Delete(path);
        store.BodyDeleted(path);

        var after = store.GetOrLoad(path);
        Assert.False(after.IsUsable && after.Model.Variables.Count > 0, "The deleted diagram is still held as if it existed.");
    }

    [Fact]
    public async Task ABodyDeletedUnderAnOpenDiagram_IsNoLongerDrawn_ThroughTheWatcher()
    {
        // The same rule end to end, through the real bridge and reloader. A bridge that still
        // reloads on the delete leaves the store keeping the last good diagram.
        var path = IoPath.Combine(_workspace, "loop.cld");
        await File.WriteAllTextAsync(path, Text, TestContext.Current.CancellationToken);
        var store = new CausalLoopDocumentStore();
        var origin = new DiagramOrigin("test", "causal-loop");
        using var bridge = new DiagramDocumentReloadBridge([new CausalLoopDocumentReloader(origin, store)]);
        Assert.True(store.GetOrLoad(path).IsUsable);
        bridge.Track(_workspace, path, registrationPath: null, origin);

        File.Delete(path);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (store.GetOrLoad(path).IsUsable && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.False(store.GetOrLoad(path).IsUsable, "The deleted diagram is still held as if it existed.");
    }

    [Fact]
    public async Task ReloadsRacingAnExternalPublishOfTheSameDiagram_NeverLoseIt()
    {
        // The measured race as a guard, in causal-loop's shape: an external writer republishes an
        // UNCHANGED diagram while the store reloads it - a text editor saving over the file. A
        // publish either lands whole or leaves the body missing for an instant; a missing body is
        // confirmed before it counts as gone, so the diagram must never be lost.
        var path = IoPath.Combine(_workspace, "loop.cld");
        await File.WriteAllTextAsync(path, Text, TestContext.Current.CancellationToken);
        // The retry's wait taken away, and only here: every reload that finds the body renamed away
        // mid-publish now waits between attempts, and at 50 ms this test went from 0.8 s to 8.4 s
        // (2026-09-25). What it guards is that the diagram is never lost, which keep-last-good decides,
        // not how long a retry waits; the store's own attempts are kept.
        var store = new CausalLoopDocumentStore(SharedDocumentReader.ReadAllText, CausalLoopDocumentStore.DefaultReadAttempts, TimeSpan.Zero);
        Assert.True(store.GetOrLoad(path).IsUsable);

        using var stop = new CancellationTokenSource();
        var publishes = 0;
        var writer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    AdpFileWriter.Save(path, Text);
                    Interlocked.Increment(ref publishes);
                }
                catch (IOException)
                {
                    // A refused publish is not what this guard measures.
                }
            }
        }, TestContext.Current.CancellationToken);

        // Reload only once the writer is publishing. On a loaded runner the 3000 reloads can finish
        // before the writer's task is even scheduled, and then nothing raced at all.
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref publishes) > 0, TimeSpan.FromSeconds(10)),
            "The arrangement failed: the external writer never started publishing.");

        const int reloads = 3000;
        var lost = 0;
        try
        {
            for (var i = 0; i < reloads; i++)
            {
                store.Reload(path);
                if (!store.GetOrLoad(path).IsUsable)
                {
                    lost++;
                }
            }
        }
        finally
        {
            await stop.CancelAsync();
            await writer;
        }

        Assert.True(publishes > 0, "The arrangement failed: the external writer never published, so nothing raced.");
        Assert.True(lost == 0, $"{lost} of {reloads} reloads racing {publishes} external publishes lost the diagram.");
    }

    [Fact]
    public void ASaveOfADiagramThatCouldNotBeReadEvenOnce_RefusesAndLeavesTheFileAlone()
    {
        // The case keeping the last good entry cannot cover: the very first read fails, so there
        // is nothing good to keep and the entry is Unreadable. Writing it would put its empty
        // document over the only copy of the diagram there is.
        var path = IoPath.Combine(_workspace, "loop.cld");
        File.WriteAllText(path, Text);
        var store = new CausalLoopDocumentStore();

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(store.GetOrLoad(path).IsUsable, "The arrangement failed: the first read succeeded.");
        }

        var answer = store.Save(path, store.GetOrLoad(path));

        Assert.NotEqual("", answer.Error);
        Assert.Equal(Text, File.ReadAllText(path));
    }
}
