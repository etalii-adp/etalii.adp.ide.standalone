using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The store's lifecycle: load-once, byte-identical saves of untouched documents, errors carried
/// rather than thrown, and broken files refused on save (rdf-diagram Requirements 1.3 and 1.5).
/// </summary>
public class RdfDocumentStoreTests : IDisposable
{
    private readonly string _root;
    private readonly RdfDocumentStore _store = new();

    public RdfDocumentStoreTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string CopyFixture(string name)
    {
        var destination = IoPath.Combine(_root, name);
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name), destination);
        return destination;
    }

    [Theory]
    [InlineData("constructs.ttl")]
    [InlineData("crlf-line-endings.ttl")]
    [InlineData("lf-line-endings.ttl")]
    [InlineData("no-trailing-newline.ttl")]
    [InlineData("tied-line-endings.ttl")]
    [InlineData("simple.nt")]
    public void SavingAnUntouchedDocument_LeavesTheFileByteIdentical(string name)
    {
        // Arrange.
        var path = CopyFixture(name);
        var original = File.ReadAllBytes(path);
        var entry = _store.GetOrLoad(path);

        // Act. Nothing is edited between the load and the save, which is what makes this test about
        // byte identity rather than about edits surviving - see the LostEdit test beside it for the
        // case an untouched document cannot express.
        var refusal = _store.Save(path, entry);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void AMissingFile_OpensAsAnEmptyUsableDocument()
    {
        // Arrange & act.
        var entry = _store.GetOrLoad(IoPath.Combine(_root, "not-yet.ttl"));

        // Assert.
        // A freshly created diagram opens, and the first save creates the file.
        Assert.True(entry.IsUsable);
        Assert.Empty(entry.Document.Lines);
        Assert.Empty(entry.Model.Triples);
    }

    [Fact]
    public void AFileThatIsNotTurtle_CarriesTheErrorAndItsLine_AndSaveRefuses()
    {
        // Arrange.
        var path = CopyFixture("broken.ttl");
        var original = File.ReadAllBytes(path);

        // Act.
        var entry = _store.GetOrLoad(path);
        var refusal = _store.Save(path, entry);

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.NotEqual(0, entry.ErrorLine);
        Assert.NotEqual("", refusal);
        Assert.Contains("broken.ttl", refusal);
        // Refusing to save left the broken file exactly as it was.
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void OneDocumentIsLoadedOnce_AndSharedByEveryReading()
    {
        // Arrange.
        var path = CopyFixture("constructs.ttl");

        // Act.
        var first = _store.GetOrLoad(path);
        var second = _store.GetOrLoad(path);

        // Assert.
        // Reference equality: two readings of one file share one document and one model.
        Assert.Same(first, second);
    }

    [Fact]
    public void ReloadAfterAnExternalChange_ReplacesTheEntryAndTellsTheSessions()
    {
        // Arrange.
        var path = CopyFixture("crlf-line-endings.ttl");
        _store.GetOrLoad(path);
        File.WriteAllText(path, "@prefix ex: <http://example.org/> .\r\nex:x ex:y ex:z .\r\n");
        RdfDocumentChangedEventArgs? raised = null;
        _store.Changed += (_, args) => raised = args;

        // Act.
        _store.Reload(path);

        // Assert.
        Assert.NotNull(raised);
        Assert.Equal(path, raised.Path);
        var triple = Assert.Single(raised.Entry.Model.Triples);
        Assert.Equal("http://example.org/x", Assert.IsType<IriTerm>(triple.Subject).Iri);
    }

    [Fact]
    public void ForgettingADocument_MakesTheNextOpenReadAfresh()
    {
        // Arrange.
        var path = CopyFixture("crlf-line-endings.ttl");
        var before = _store.GetOrLoad(path);
        File.WriteAllText(path, "@prefix ex: <http://example.org/> .\r\nex:x ex:y ex:z .\r\n");

        // Act.
        _store.Forget(path);
        var after = _store.GetOrLoad(path);

        // Assert.
        Assert.NotSame(before, after);
        var triple = Assert.Single(after.Model.Triples);
        Assert.Equal("http://example.org/x", Assert.IsType<IriTerm>(triple.Subject).Iri);
    }
}
