using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// What the Wardley map store does that the shared contract does not say: one document shared by
/// every connection whatever the path's casing, a missing sibling opened as an empty map and created
/// on the first save, the bytes a save keeps, and a re-delivery that writes nothing.
/// </summary>
public sealed class WardleyDocumentStoreSharingTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), $"wardley-store-{Guid.NewGuid():N}");
    private readonly WardleyDocumentStore _store = new();

    public WardleyDocumentStoreSharingTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
    }

    private string Write(string name, string text)
    {
        var path = IoPath.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void GetOrLoad_ReturnsTheSameInstance_SoTwoSessionsShareOneDocument()
    {
        // Arrange.
        var path = Write("map.owm", "title Shared\n");

        // Act.
        var first = _store.GetOrLoad(path).Document;
        var second = _store.GetOrLoad(path).Document;

        // Assert. Requirement 10.6 - an edit through one connection must be visible to another
        // without a reload, which only holds if they hold one document between them.
        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrLoad_OpensAMissingFileAsAnEmptyDocument_RatherThanThrowing()
    {
        // Arrange. Requirement 2.4 - the `.owm` sibling may not have been written yet.
        var path = IoPath.Combine(_folder, "absent.owm");

        // Act.
        var document = _store.GetOrLoad(path).Document;

        // Assert.
        Assert.Equal("", document.Text);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        // Arrange.
        var path = Write("map.owm", "title Before\n");
        var document = _store.GetOrLoad(path).Document;
        WardleyWriter.ReplaceLine(document, 1, "title After");

        // Act.
        _store.Save(path, document);

        // Assert. The temp-then-move discipline must not litter the project folder, and the
        // name it uses is one HierarchyModel already ignores.
        Assert.Equal(["map.owm"], Directory.GetFiles(_folder).Select(IoPath.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Save_PreservesTheDocumentsBytes_IncludingCrlfAndAMissingFinalNewline()
    {
        // Arrange. Requirement 3.1 end to end: through the store, not just the document.
        const string text = "title Kept\r\nanchor A [0.9, 0.5]";
        var path = Write("map.owm", text);

        // Act.
        var document = _store.GetOrLoad(path).Document;
        _store.Save(path, document);

        // Assert.
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Fact]
    public void Save_CreatesTheFile_WhenTheSiblingDidNotExistYet()
    {
        // Arrange. Requirement 2.4 - the sibling is created on the first save.
        var path = IoPath.Combine(_folder, "new.owm");
        var document = _store.GetOrLoad(path).Document;
        WardleyWriter.InsertLine(document, 1, "title Created");

        // Act.
        _store.Save(path, document);

        // Assert.
        Assert.True(File.Exists(path));
        // CRLF, and terminated: a document with no line breaks to learn from takes the core line
        // document's ending, the same on any machine. This module wrote LF here until it moved
        // onto the core line document (code-structure behaviour change B-05).
        Assert.Equal("title Created\r\n", File.ReadAllText(path));
    }

    [Fact]
    public void Touch_RaisesChanged_WithoutWritingAnything()
    {
        // Arrange.
        var path = Write("map.owm", "title X\n");
        WardleyWriter.ReplaceLine(_store.GetOrLoad(path).Document, 1, "title Edited in memory");
        var raised = 0;
        _store.Changed += (_, _) => raised++;

        // Act.
        _store.Touch(path);

        // Assert. The file is untouched; only the sessions are told to re-deliver.
        Assert.Equal(1, raised);
        Assert.Equal("title X\n", File.ReadAllText(path));
    }

    [Fact]
    public void GetOrLoad_IsCaseInsensitiveOnPath_SoOneFileIsOneDocument()
    {
        // Arrange. Windows hands the same file back under either casing; two entries for it
        // would be two documents drifting apart.
        var path = Write("Map.owm", "title X\n");

        // Act.
        var first = _store.GetOrLoad(path).Document;
        var second = _store.GetOrLoad(path.Replace("Map.owm", "map.owm", StringComparison.Ordinal)).Document;

        // Assert.
        Assert.Same(first, second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetOrLoad_RefusesAPathThatIsNotOne(string? path)
    {
        // Act and assert.
        Assert.ThrowsAny<ArgumentException>(() => _store.GetOrLoad(path!).Document);
    }
}
