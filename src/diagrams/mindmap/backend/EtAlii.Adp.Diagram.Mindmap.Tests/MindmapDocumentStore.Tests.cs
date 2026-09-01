using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// What the store does with the files the world actually hands it - above all the ones that
/// are not yet, or no longer, a map.
/// </summary>
public class MindmapDocumentStoreTests : IDisposable
{
    private readonly string _root;
    private readonly MindmapDocumentStore _store = new();

    public MindmapDocumentStoreTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public void GetOrLoad_AnEmptyFile_OpensAsAnEmptyMapNamedAfterIt()
    {
        // Arrange.
        // A zero-byte .mm - a file created by hand, or a body a tool left blank - used to
        // throw "Data at the root level is invalid" on every open attempt. It is a map that
        // has not been written yet, which is Requirement 2.5's recoverable state.
        var bodyPath = IoPath.Combine(_root, "design.mm");
        File.WriteAllText(bodyPath, "");

        // Act.
        var document = _store.GetOrLoad(bodyPath);

        // Assert.
        Assert.Equal("design", document.Root.Text);
        Assert.Empty(document.Root.Children);
    }

    [Fact]
    public void GetOrLoad_AWhitespaceOnlyFile_OpensAsAnEmptyMapToo()
    {
        // Arrange.
        var bodyPath = IoPath.Combine(_root, "notes.mm");
        File.WriteAllText(bodyPath, " \r\n\t\n");

        // Act.
        var document = _store.GetOrLoad(bodyPath);

        // Assert.
        Assert.Equal("notes", document.Root.Text);
    }

    [Fact]
    public void GetOrLoad_AGenuinelyMalformedFile_StillFailsNamingIt()
    {
        // Arrange.
        // Requirement 3.7: real damage degrades to a clear error on that one diagram.
        var bodyPath = IoPath.Combine(_root, "broken.mm");
        File.WriteAllText(bodyPath, "<map version=\"freeplane 1.11.5\"><node TEXT=\"unclosed\"></map>");

        // Act.
        var exception = Assert.Throws<MindmapFormatException>(() => _store.GetOrLoad(bodyPath));

        // Assert.
        Assert.Contains("broken.mm", exception.Message, StringComparison.Ordinal);
    }
}
