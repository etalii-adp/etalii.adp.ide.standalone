using EtAlii.Adp.Files;
using EtAlii.Adp.Workspace;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

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
    public async Task ASaveAfterAReloadThatCouldNotRead_DoesNotWriteAnEmptyDocumentOverTheRealFile()
    {
        // Arrange: a real diagram, loaded.
        var path = IoPath.Combine(_workspace, "loop.cld");
        await File.WriteAllTextAsync(path, Text, TestContext.Current.CancellationToken);
        var store = new CausalLoopDocumentStore(new FileWriter());
        Assert.True(store.GetOrLoad(path).IsUsable);

        // Arrange, continued: a reload that cannot read the body.
        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store.Reload(path);
        }

        // Assert, first: the store still holds the diagram - the failed read did not replace it.
        // This is what keeps the canvas drawn, and it is asserted on its own because the save
        // refusal below would protect the file even without it.
        var held = store.GetOrLoad(path);
        Assert.True(held.IsUsable, $"The failed reload replaced the last good diagram: {held.Unreadable}");

        // Act: whatever triggers the next save, once the file is readable again.
        await store.SaveAsync(path, held.Document, TestContext.Current.CancellationToken);

        // Assert: the real diagram is still on disk.
        Assert.Equal(Text, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
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
        var store = new CausalLoopDocumentStore(new FileWriter());
        Assert.True(store.GetOrLoad(path).IsUsable);

        File.Delete(path);
        store.BodyDeleted(path);

        var after = store.GetOrLoad(path);
        Assert.False(after is { IsUsable: true, Model.Variables.Count: > 0 }, "The deleted diagram is still held as if it existed.");
    }

    [Fact]
    public async Task ABodyDeletedUnderAnOpenDiagram_IsNoLongerDrawn_ThroughTheWatcher()
    {
        // The same rule end to end, through the real bridge and reloader. A bridge that still
        // reloads on the delete leaves the store keeping the last good diagram.
        var path = IoPath.Combine(_workspace, "loop.cld");
        await File.WriteAllTextAsync(path, Text, TestContext.Current.CancellationToken);
        var store = new CausalLoopDocumentStore(new FileWriter());
        var origin = new ToolOrigin("test", "causal-loop");
        using var events = new ProjectFileEvents();
        using var bridge = new DiagramDocumentReloadBridge([new DiagramDocumentReloader<ICausalLoopDocumentStore>(origin, store)], events);
        Assert.True(store.GetOrLoad(path).IsUsable);
        bridge.Track(ProjectRoot.From(_workspace), path, registrationPath: null, origin);

        File.Delete(path);

        Assert.True(
            await Eventually.TrueAsync(() => store.GetOrLoad(path).Model.Variables.Count == 0, TestContext.Current.CancellationToken),
            "The deletion did not empty the diagram within the timeout.");
    }

    [Fact]
    public async Task ASaveOfADiagramThatCouldNotBeReadEvenOnce_RefusesAndLeavesTheFileAlone()
    {
        // The case keeping the last good entry cannot cover: the very first read fails, so there
        // is nothing good to keep and the entry is Unreadable. Writing it would put its empty
        // document over the only copy of the diagram there is.
        var path = IoPath.Combine(_workspace, "loop.cld");
        await File.WriteAllTextAsync(path, Text, TestContext.Current.CancellationToken);
        var store = new CausalLoopDocumentStore(new FileWriter());

        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(store.GetOrLoad(path).IsUsable, "The arrangement failed: the first read succeeded.");
        }

        var answer = await store.SaveAsync(path, store.GetOrLoad(path).Document, TestContext.Current.CancellationToken);

        Assert.NotEqual("", answer.Error);
        Assert.Equal(Text, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }
}
