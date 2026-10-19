using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The store that lets several diagrams share one document, which is the mechanism behind
/// "model once, view many" (c4-diagrams Requirement 1.2). What every module's store does is held
/// by <see cref="C4DocumentStoreTests"/>; these are the facts that are C4's own.
/// </summary>
public class C4DocumentStoreSharingTests : IDisposable
{
    private readonly string _root;
    private readonly C4DocumentStore _store = new(new FileWriter());

    public C4DocumentStoreSharingTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Diagram.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string Write(string name, string text)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }

    private const string Sample = """
        workspace "Sample" {
            model {
                s = softwareSystem "System" "Does the thing."
            }
            views {
                systemContext s "context" {
                    include *
                }
            }
        }
        """;

    [Fact]
    public async Task TwoOpensOfOnePath_ShareOneDocument()
    {
        // Act.
        // The whole point: an edit through one view must be visible to another without a
        // reload, which is only true if they hold the same instance.
        var path = Write("model.dsl", Sample);

        // Assert.
        Assert.Same((await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Document, (await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Document);
    }

    [Fact]
    public async Task AMissingFile_OpensAsAnEmptyDocument_RatherThanThrowing()
    {
        // Act.
        var document = (await _store.GetOrLoadAsync(IoPath.Combine(_root, "not-written-yet.dsl"), TestContext.Current.CancellationToken)).Document;

        // Assert.
        Assert.DoesNotContain(document.Lines, line => line.Text.Length > 0);
        Assert.Empty((await _store.GetOrLoadAsync(IoPath.Combine(_root, "not-written-yet.dsl"), TestContext.Current.CancellationToken)).Model.Elements);
    }

    [Fact]
    public async Task Save_WritesTheDocumentBack_ByteForByte()
    {
        // Arrange.
        var path = Write("model.dsl", Sample);
        await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Act.
        await _store.SaveAsync(path, (await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(Sample, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Save_OfAnEditedDocument_ChangesOnlyTheEditedLine()
    {
        // Arrange.
        var path = Write("model.dsl", Sample);
        var document = (await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Document;
        var line = document.CodeLines().First(l => l.Code.Contains("softwareSystem", StringComparison.Ordinal));
        document.ReplaceLine(line.Number, line.Text.Replace("\"System\"", "\"Renamed\"", StringComparison.Ordinal));

        await _store.SaveAsync(path, document, TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        var after = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("Renamed", after, StringComparison.Ordinal);
        Assert.Equal(Sample.Split('\n').Length, after.Split('\n').Length);
    }

    [Fact]
    public async Task Save_RaisesChanged_OnceTheWorkspaceIsReparsed()
    {
        // Arrange.
        var path = Write("model.dsl", Sample);
        var document = (await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Document;
        var line = document.CodeLines().First(l => l.Code.Contains("softwareSystem", StringComparison.Ordinal));
        document.ReplaceLine(line.Number, line.Text.Replace("\"System\"", "\"Renamed\"", StringComparison.Ordinal));

        // Act.
        string? raised = null;
        _store.Changed += (_, args) => raised = _store.Get(args.Path)!.Model.Find("s")!.Name;
        await _store.SaveAsync(path, document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Renamed", raised);
    }

    [Fact]
    public async Task TheModel_ReflectsASave_SoTheNextReaderSeesTheEdit()
    {
        // Arrange and act.
        var path = Write("model.dsl", Sample);
        var document = (await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Document;
        var line = document.CodeLines().First(l => l.Code.Contains("softwareSystem", StringComparison.Ordinal));
        document.ReplaceLine(line.Number, line.Text.Replace("\"System\"", "\"Renamed\"", StringComparison.Ordinal));
        await _store.SaveAsync(path, document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Renamed", (await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Model.Find("s")!.Name);
    }

    [Fact]
    public async Task ATouchOfADocumentNotReadYet_ReadsItBeforeTheSessionsHear()
    {
        // Arrange: a drag's sidecar written for a model this store has not read yet.
        var path = Write("model.dsl", Sample);
        DocumentEntry<LineDocument, C4Workspace>? heard = null;
        _store.Changed += (_, _) => heard = _store.Get(path);

        // Act.
        await _store.TouchAsync(path, TestContext.Current.CancellationToken);

        // Assert: a session re-delivering on the touch finds the model held, as it renders only
        // what is held.
        Assert.Equal("System", heard?.Model.Find("s")?.Name);
    }

    [Fact]
    public async Task Save_CreatesTheFolder_WhenTheBodyLivesBesideNothingYet()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "nested", "model.dsl");
        await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Act.
        await _store.SaveAsync(path, (await _store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(File.Exists(path));
    }
}
