using EtAlii.Adp.Files;
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
    private readonly TestFolder _testFolder = TestFolder.Create("adp-depgraph-store");
    private readonly string _workspace;

    public DependencyGraphDocumentStoreSharedReadTests() => _workspace = _testFolder.Path;

    public void Dispose()
    {
        _testFolder.Dispose();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetOrLoad_ReadsADocumentAnEditorIsStillWriting()
    {
        // Arrange: the handle every save holds - write access, sharing only reads, the mode
        // File.WriteAllText opens with.
        const string text = "dependencies: 1\nelements:\n  - id: aaa\n    label: Node\n    x: 10\n    row: 0\n";
        var path = IoPath.Combine(_workspace, "graph.dgr");
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        await using var editor = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);

        // Act.
        var entry = await new DependencyGraphDocumentStore(new FileWriter()).GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Assert: the document itself, not the empty fallback.
        Assert.True(entry.IsUsable);
        Assert.Equal(text, entry.Document.Text);
    }
}
