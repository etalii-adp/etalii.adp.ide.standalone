using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

public sealed class WardleyDocumentStoreTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), $"wardley-store-{Guid.NewGuid():N}");
    private readonly WardleyDocumentStore _store = new();

    public WardleyDocumentStoreTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A scratch folder that outlives the test is untidy, never a failure.
        }
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
        var first = _store.GetOrLoad(path);
        var second = _store.GetOrLoad(path);

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
        var document = _store.GetOrLoad(path);

        // Assert.
        Assert.Equal("", document.ToText());
    }

    [Fact]
    public void Save_WritesTheDocumentBack()
    {
        // Arrange.
        var path = Write("map.owm", "title Before\n");
        var document = _store.GetOrLoad(path);
        document.ReplaceLine(1, "title After");

        // Act.
        _store.Save(path);

        // Assert.
        Assert.Equal("title After\n", File.ReadAllText(path));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        // Arrange.
        var path = Write("map.owm", "title Before\n");
        _store.GetOrLoad(path).ReplaceLine(1, "title After");

        // Act.
        _store.Save(path);

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
        _store.GetOrLoad(path);
        _store.Save(path);

        // Assert.
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Fact]
    public void Save_CreatesTheFile_WhenTheSiblingDidNotExistYet()
    {
        // Arrange. Requirement 2.4 - the sibling is created on the first save.
        var path = IoPath.Combine(_folder, "new.owm");
        var document = _store.GetOrLoad(path);
        document.InsertLine(1, "title Created");

        // Act.
        _store.Save(path);

        // Assert.
        Assert.True(File.Exists(path));
        Assert.Equal("title Created", File.ReadAllText(path));
    }

    [Fact]
    public void Save_RaisesChanged()
    {
        // Arrange.
        var path = Write("map.owm", "title X\n");
        _store.GetOrLoad(path);
        var raised = new List<string>();
        _store.Changed += (_, args) => raised.Add(args.Path);

        // Act.
        _store.Save(path);

        // Assert.
        Assert.Equal([path], raised);
    }

    [Fact]
    public void Touch_RaisesChanged_WithoutWritingAnything()
    {
        // Arrange.
        var path = Write("map.owm", "title X\n");
        _store.GetOrLoad(path).ReplaceLine(1, "title Edited in memory");
        var raised = 0;
        _store.Changed += (_, _) => raised++;

        // Act.
        _store.Touch(path);

        // Assert. The file is untouched; only the sessions are told to re-deliver.
        Assert.Equal(1, raised);
        Assert.Equal("title X\n", File.ReadAllText(path));
    }

    [Fact]
    public void Forget_MakesTheNextOpenReadFromDiskAgain()
    {
        // Arrange.
        var path = Write("map.owm", "title Original\n");
        var first = _store.GetOrLoad(path);
        File.WriteAllText(path, "title Changed outside\n");

        // Act.
        _store.Forget(path);
        var second = _store.GetOrLoad(path);

        // Assert.
        Assert.NotSame(first, second);
        Assert.Equal("title Changed outside\n", second.ToText());
    }

    [Fact]
    public void Reload_PicksUpAnExternalEdit_AndTellsTheSessions()
    {
        // Arrange. Requirement 10.7 - a `git pull` or an edit in another editor.
        var path = Write("map.owm", "title Original\n");
        _store.GetOrLoad(path);
        File.WriteAllText(path, "title Changed outside\n");
        var raised = 0;
        _store.Changed += (_, _) => raised++;

        // Act.
        _store.Reload(path);

        // Assert.
        Assert.Equal(1, raised);
        Assert.Equal("title Changed outside\n", _store.GetOrLoad(path).ToText());
    }

    [Fact]
    public void GetOrLoad_IsCaseInsensitiveOnPath_SoOneFileIsOneDocument()
    {
        // Arrange. Windows hands the same file back under either casing; two entries for it
        // would be two documents drifting apart.
        var path = Write("Map.owm", "title X\n");

        // Act.
        var first = _store.GetOrLoad(path);
        var second = _store.GetOrLoad(path.Replace("Map.owm", "map.owm", StringComparison.Ordinal));

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
        Assert.ThrowsAny<ArgumentException>(() => _store.GetOrLoad(path!));
    }
}
