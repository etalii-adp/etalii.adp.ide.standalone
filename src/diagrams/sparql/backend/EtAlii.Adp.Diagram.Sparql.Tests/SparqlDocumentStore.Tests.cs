using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// The read-only store's lifecycle: load-once, errors carried rather than thrown, external
/// changes picked up on reload - and the file's bytes untouched by everything the store does,
/// which for this store is not a guarantee about careful writing but about the absence of any.
/// </summary>
public class SparqlDocumentStoreTests : IDisposable
{
    private readonly string _root;
    private readonly SparqlDocumentStore _store = new();

    public SparqlDocumentStoreTests()
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
    [InlineData("constructs.rq")]
    [InlineData("groups.rq")]
    [InlineData("crlf-line-endings.rq")]
    [InlineData("no-trailing-newline.rq")]
    public void LoadingAndReloading_LeaveTheFileByteIdentical(string name)
    {
        // Arrange.
        var path = CopyFixture(name);
        var original = File.ReadAllBytes(path);

        // Act.
        var entry = _store.GetOrLoad(path);
        _store.Reload(path);
        _store.Forget(path);
        _store.GetOrLoad(path);

        // Assert.
        Assert.True(entry.IsUsable);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void AMissingFile_OpensUnavailableNamingTheTextEditor()
    {
        // Arrange & act.
        var entry = _store.GetOrLoad(IoPath.Combine(_root, "not-there.rq"));

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.Contains("does not exist", entry.Error);
        Assert.Contains("text editor", entry.Error);
    }

    [Fact]
    public void AFileThatDoesNotParse_OpensUnavailableWithLineAndReason()
    {
        // Arrange.
        var path = CopyFixture("broken.rq");

        // Act.
        var entry = _store.GetOrLoad(path);

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.Equal(4, entry.ErrorLine);
        Assert.Contains("undeclared", entry.Error);
        Assert.Same(SparqlQueryModel.Empty, entry.Model);
    }

    [Fact]
    public void AnUpdateDocument_OpensUnavailableNamingSparqlUpdate()
    {
        // Arrange.
        var path = CopyFixture("update-document.rq");

        // Act.
        var entry = _store.GetOrLoad(path);

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.Contains("SPARQL Update", entry.Error);
    }

    [Fact]
    public void AnExternalChange_ReachesTheEntryThroughReload()
    {
        // Arrange.
        var path = CopyFixture("crlf-line-endings.rq");
        var before = _store.GetOrLoad(path);
        Assert.Single(before.Model.Where.Patterns);

        SparqlDocumentChangedEventArgs? raised = null;
        _store.Changed += (_, args) => raised = args;

        // Act.
        File.WriteAllText(path, "PREFIX ex: <http://example.org/>\r\nSELECT ?s\r\nWHERE { ?s ex:p ?o . ?s ex:q ?o2 }\r\n");
        _store.Reload(path);

        // Assert.
        Assert.NotNull(raised);
        Assert.Equal(path, raised.Path);
        Assert.Equal(2, raised.Entry.Model.Where.Patterns.Count);
    }

    [Fact]
    public void LoadingIsOnce_UntilForgotten()
    {
        // Arrange.
        var path = CopyFixture("lf-line-endings.rq");
        var first = _store.GetOrLoad(path);

        // Act: an unseen external change is invisible until reload or forget - the entry is kept.
        File.WriteAllText(path, "ASK { ?s ?p ?o }");
        var second = _store.GetOrLoad(path);
        _store.Forget(path);
        var third = _store.GetOrLoad(path);

        // Assert.
        Assert.Same(first, second);
        Assert.Equal(SparqlQueryForm.Ask, third.Model.Form);
    }
}
