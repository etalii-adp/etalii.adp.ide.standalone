using EtAlii.Adp.Documents;
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
    private readonly TestFolder _testFolder = TestFolder.Create("sparql-document-store");
    private readonly string _root;
    private readonly SparqlDocumentStore _store = new();

    public SparqlDocumentStoreTests()
    {
        _root = _testFolder.Path;
    }

    public void Dispose()
    {
        _testFolder.Dispose();
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
    public async Task LoadingAndReloading_LeaveTheFileByteIdentical(string name)
    {
        // Arrange.
        var path = CopyFixture(name);
        var original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var entry = await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        await _store.ReloadAsync(path, TestContext.Current.CancellationToken);
        _store.Forget(path);
        await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(entry.IsUsable);
        Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMissingFile_OpensUnavailableNamingTheTextEditor()
    {
        // Arrange & act.
        var entry = await _store.GetOrLoadAsync(IoPath.Combine(_root, "not-there.rq"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.Contains("does not exist", entry.Unreadable);
        Assert.Contains("text editor", entry.Unreadable);
    }

    [Fact]
    public async Task AFileThatDoesNotParse_OpensUnavailableWithItsReason()
    {
        // Arrange.
        var path = CopyFixture("broken.rq");

        // Act.
        var entry = await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.Contains("undeclared", entry.Unreadable);
        Assert.Same(SparqlQueryModel.Empty, entry.Model);
        var problem = Assert.Single(entry.Problems);
        // broken.rq uses the undeclared prefix on its fourth line, which is zero-based line 3.
        Assert.Equal((3, entry.Unreadable), (problem.Line, problem.Message));
    }

    [Fact]
    public async Task AnUpdateDocument_OpensUnavailableNamingSparqlUpdate()
    {
        // Arrange.
        var path = CopyFixture("update-document.rq");

        // Act.
        var entry = await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.Contains("SPARQL Update", entry.Unreadable);
    }

    [Fact]
    public async Task AnExternalChange_ReachesTheEntryThroughReload()
    {
        // Arrange.
        var path = CopyFixture("crlf-line-endings.rq");
        var before = await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        Assert.Single(before.Model.Where.Patterns);

        DocumentChangedEventArgs? raised = null;
        _store.Changed += (_, args) => raised = args;

        // Act.
        await File.WriteAllTextAsync(path, "PREFIX ex: <http://example.org/>\r\nSELECT ?s\r\nWHERE { ?s ex:p ?o . ?s ex:q ?o2 }\r\n", TestContext.Current.CancellationToken);
        await _store.ReloadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotNull(raised);
        Assert.Equal(path, raised.Path);
        Assert.Equal(2, (await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Model.Where.Patterns.Count);
    }

    [Fact]
    public async Task LoadingIsOnce_UntilForgotten()
    {
        // Arrange.
        var path = CopyFixture("lf-line-endings.rq");
        var first = await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Act: an unseen external change is invisible until reload or forget - the entry is kept.
        await File.WriteAllTextAsync(path, "ASK { ?s ?p ?o }", TestContext.Current.CancellationToken);
        var second = await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        _store.Forget(path);
        var third = await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Same(first, second);
        Assert.Equal(SparqlQueryForm.Ask, third.Model.Form);
    }
}
