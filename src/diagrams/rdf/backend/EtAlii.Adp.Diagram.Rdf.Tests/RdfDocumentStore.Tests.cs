using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
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
    private readonly RdfDocumentStore _store = new(new FileWriter());

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
    public async Task SavingAnUntouchedDocument_LeavesTheFileByteIdentical(string name)
    {
        // Arrange.
        var path = CopyFixture(name);
        var original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var entry = await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Act. Nothing is edited between the load and the save, which is what makes this test about
        // byte identity rather than about edits surviving - see the LostEdit test beside it for the
        // case an untouched document cannot express.
        var refusal = await _store.SaveAsync(path, entry.Document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", refusal.Error);
        Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMissingFile_OpensAsAnEmptyUsableDocument()
    {
        // Arrange & act.
        var entry = await _store.GetOrLoadAsync(IoPath.Combine(_root, "not-yet.ttl"), TestContext.Current.CancellationToken);

        // Assert.
        // A freshly created diagram opens, and the first save creates the file.
        Assert.True(entry.IsUsable);
        Assert.Empty(entry.Document.Lines);
        Assert.Empty(entry.Model.Triples);
    }

    [Fact]
    public async Task AFileThatIsNotTurtle_CarriesTheErrorAndItsLine_AndSaveRefuses()
    {
        // Arrange.
        var path = CopyFixture("broken.ttl");
        var original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        // Act.
        var entry = await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        var refusal = await _store.SaveAsync(path, entry.Document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.Contains("at line", entry.Unreadable, StringComparison.Ordinal);
        var problem = Assert.Single(entry.Problems);
        Assert.Equal(entry.Unreadable, $"it does not parse at line {problem.Line + 1}. {problem.Message}");
        Assert.NotEqual("", refusal.Error);
        Assert.Contains("broken.ttl", refusal.Error);
        // Refusing to save left the broken file exactly as it was.
        Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OneDocumentIsLoadedOnce_AndSharedByEveryReading()
    {
        // Arrange.
        var path = CopyFixture("constructs.ttl");

        // Act.
        var first = await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        var second = await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        // Reference equality: two readings of one file share one document and one model.
        Assert.Same(first, second);
    }

    [Fact]
    public async Task ReloadAfterAnExternalChange_ReplacesTheEntryAndTellsTheSessions()
    {
        // Arrange.
        var path = CopyFixture("crlf-line-endings.ttl");
        await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, "@prefix ex: <http://example.org/> .\r\nex:x ex:y ex:z .\r\n", TestContext.Current.CancellationToken);
        DocumentChangedEventArgs? raised = null;
        _store.Changed += (_, args) => raised = args;

        // Act.
        await _store.ReloadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotNull(raised);
        Assert.Equal(path, raised.Path);
        var triple = Assert.Single((await _store.GetOrLoadAsync(raised.Path, TestContext.Current.CancellationToken)).Model.Triples);
        Assert.Equal("http://example.org/x", Assert.IsType<IriTerm>(triple.Subject).Iri);
    }

    [Fact]
    public async Task ForgettingADocument_MakesTheNextOpenReadAfresh()
    {
        // Arrange.
        var path = CopyFixture("crlf-line-endings.ttl");
        var before = await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, "@prefix ex: <http://example.org/> .\r\nex:x ex:y ex:z .\r\n", TestContext.Current.CancellationToken);

        // Act.
        _store.Forget(path);
        var after = await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotSame(before, after);
        var triple = Assert.Single(after.Model.Triples);
        Assert.Equal("http://example.org/x", Assert.IsType<IriTerm>(triple.Subject).Iri);
    }
}
