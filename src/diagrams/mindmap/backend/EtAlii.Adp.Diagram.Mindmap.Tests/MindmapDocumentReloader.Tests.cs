using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// A body that could not be read is kept, and a deleted body is cleared - the pairing the shared
/// lifecycle depends on (backend-centralization R2.4, R2.5), which task 6 brings to this store in
/// one change and which <see cref="MindmapDocumentReloader"/> must forward.
/// </summary>
/// <remarks>
/// <b>The reloader is held as <see cref="IDiagramDocumentReloader"/>, and that is load-bearing.</b> A
/// default interface member is callable only through the interface. Held as the class, removing the
/// <c>BodyDeleted</c> override would stop this file compiling instead of making a test fail, so the
/// defect could never be seen red. Do not "simplify" the declared type.
/// </remarks>
public sealed class MindmapDocumentReloaderTests : IDisposable
{
    private const string Map = """
        <map version="freeplane 1.11.5">
          <node TEXT="Design" ID="ID_1">
            <node TEXT="Layout" ID="ID_2"/>
          </node>
        </map>
        """;

    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.MindmapDocumentReloaderTests", Guid.NewGuid().ToString("N"));

    public MindmapDocumentReloaderTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Body, Map);
    }

    private string Body => IoPath.Combine(_folder, "design.mm");

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
    }

    [Fact]
    public void AMissingBody_IsKeptOnReload_AndClearedOnlyByTheWatchersDelete()
    {
        // Arrange.
        var store = new MindmapDocumentStore();
        IDiagramDocumentReloader reloader = new MindmapDocumentReloader(store);
        Assert.NotEmpty(store.GetOrLoad(Body).Root.Children);
        File.Delete(Body);

        // Act: a reload that finds no file.
        reloader.Reload(_folder, Body);

        // Assert: kept until the absence is confirmed (R2.5's first half). A body missing on a
        // reload is far more often a publish in flight than a deletion, so the last good map stays.
        // This is NOT R2.4, which is a body present but unreadable - see the next test.
        Assert.NotEmpty(store.GetOrLoad(Body).Root.Children);

        // Act: the watcher's evidence that it is gone.
        reloader.BodyDeleted(_folder, Body);

        // Assert: cleared, to what a first open of a missing body shows (R2.5) - an empty map named
        // after the file.
        var cleared = store.GetOrLoad(Body);
        Assert.Empty(cleared.Root.Children);
        Assert.Equal("design", cleared.Root.Text);
    }

    [Fact]
    public void AnUnreadableBody_IsKeptOnReload_AndTellsNobody()
    {
        // Arrange.
        var store = new MindmapDocumentStore();
        IDiagramDocumentReloader reloader = new MindmapDocumentReloader(store);
        Assert.NotEmpty(store.GetOrLoad(Body).Root.Children);
        var told = 0;
        store.Changed += (_, _) => told++;

        // Act: a reload while another program holds the file with no sharing at all.
        using (new FileStream(Body, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            reloader.Reload(_folder, Body);
        }

        // Assert: the last good map is kept, not replaced by an empty one (R2.4) - before the shared
        // lifecycle this store threw here instead - and since nothing a session shows has changed, no
        // session is told.
        Assert.NotEmpty(store.GetOrLoad(Body).Root.Children);
        Assert.Equal(0, told);
    }

    [Fact]
    public void ADeletion_TellsTheSessions()
    {
        // Arrange.
        var store = new MindmapDocumentStore();
        IDiagramDocumentReloader reloader = new MindmapDocumentReloader(store);
        store.GetOrLoad(Body);
        var told = new List<string>();
        store.Changed += (_, args) => told.Add(args.BodyPath);
        File.Delete(Body);

        // Act.
        reloader.BodyDeleted(_folder, Body);

        // Assert.
        Assert.Equal([Body], told);
    }
}
