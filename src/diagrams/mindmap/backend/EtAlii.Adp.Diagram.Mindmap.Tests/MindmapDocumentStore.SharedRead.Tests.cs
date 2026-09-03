using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// The store's read side against the world's write side: a load must succeed while a writer
/// holds the file, per the sharing discipline SharedDocumentReader carries. Windows enforces
/// sharing, so this guard bites there; here a refused read did not even degrade - Load has no
/// catch, so the IOException flew out of the watcher's Reload.
/// </summary>
public class MindmapDocumentStoreSharedReadTests : IDisposable
{
    private readonly string _root;
    private readonly MindmapDocumentStore _store = new();

    public MindmapDocumentStoreSharedReadTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public void GetOrLoad_ReadsAMapAnEditorIsStillWriting()
    {
        // Arrange: the handle every save holds - write access, sharing only reads, the mode
        // File.WriteAllText opens with.
        var bodyPath = IoPath.Combine(_root, "held.mm");
        File.WriteAllText(bodyPath, "<map version=\"freeplane 1.11.5\"><node TEXT=\"RootX\"/></map>");
        using var editor = new FileStream(bodyPath, FileMode.Open, FileAccess.Write, FileShare.Read);

        // Act.
        var document = _store.GetOrLoad(bodyPath);

        // Assert: the map itself, not the empty fallback named after the file.
        Assert.Equal("RootX", document.Root.Text);
    }
}
