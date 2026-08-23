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
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void GetOrLoad_AnEmptyFile_OpensAsAnEmptyMapNamedAfterIt()
    {
        // A zero-byte .mm - a file created by hand, or a body a tool left blank - used to
        // throw "Data at the root level is invalid" on every open attempt. It is a map that
        // has not been written yet, which is Requirement 2.5's recoverable state.
        var bodyPath = IoPath.Combine(_root, "design.mm");
        File.WriteAllText(bodyPath, "");

        var document = _store.GetOrLoad(bodyPath);

        Assert.Equal("design", document.Root.Text);
        Assert.Empty(document.Root.Children);
    }

    [Fact]
    public void GetOrLoad_AWhitespaceOnlyFile_OpensAsAnEmptyMapToo()
    {
        var bodyPath = IoPath.Combine(_root, "notes.mm");
        File.WriteAllText(bodyPath, " \r\n\t\n");

        var document = _store.GetOrLoad(bodyPath);

        Assert.Equal("notes", document.Root.Text);
    }

    [Fact]
    public void GetOrLoad_AGenuinelyMalformedFile_StillFailsNamingIt()
    {
        // Requirement 3.7: real damage degrades to a clear error on that one diagram.
        var bodyPath = IoPath.Combine(_root, "broken.mm");
        File.WriteAllText(bodyPath, "<map version=\"freeplane 1.11.5\"><node TEXT=\"unclosed\"></map>");

        var exception = Assert.Throws<MindmapFormatException>(() => _store.GetOrLoad(bodyPath));

        Assert.Contains("broken.mm", exception.Message, StringComparison.Ordinal);
    }
}
