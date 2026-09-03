using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// The store's read side against the world's write side: a load must succeed while a writer
/// holds the file, per the sharing discipline SharedDocumentReader carries. Windows enforces
/// sharing, so this guard bites there; a load refused here used to become an empty document
/// pushed over a full one.
/// </summary>
public class DependencyGraphDocumentStoreSharedReadTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-depgraph-store-" + Guid.NewGuid().ToString("N"));

    public DependencyGraphDocumentStoreSharedReadTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetOrLoad_ReadsADocumentAnEditorIsStillWriting()
    {
        // Arrange: the handle every save holds - write access, sharing only reads, the mode
        // File.WriteAllText opens with.
        const string text = "dependencies: 1\nelements:\n  - id: aaa\n    label: Node\n    x: 10\n    row: 0\n";
        var path = IoPath.Combine(_workspace, "graph.dgr");
        File.WriteAllText(path, text);
        using var editor = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);

        // Act.
        var entry = new DependencyGraphDocumentStore().GetOrLoad(path);

        // Assert: the document itself, not the empty fallback.
        Assert.True(entry.IsUsable);
        Assert.Equal(text, entry.Document.Text);
    }
}
