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
    private readonly C4DocumentStore _store = new();

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
    public void TwoOpensOfOnePath_ShareOneDocument()
    {
        // Act.
        // The whole point: an edit through one view must be visible to another without a
        // reload, which is only true if they hold the same instance.
        var path = Write("model.dsl", Sample);

        // Assert.
        Assert.Same(_store.GetOrLoad(path).Document, _store.GetOrLoad(path).Document);
    }

    [Fact]
    public void AMissingFile_OpensAsAnEmptyDocument_RatherThanThrowing()
    {
        // Act.
        var document = _store.GetOrLoad(IoPath.Combine(_root, "not-written-yet.dsl")).Document;

        // Assert.
        Assert.DoesNotContain(document.Lines, line => line.Length > 0);
        Assert.Empty(_store.GetOrLoad(IoPath.Combine(_root, "not-written-yet.dsl")).Model.Elements);
    }

    [Fact]
    public void Save_WritesTheDocumentBack_ByteForByte()
    {
        // Arrange.
        var path = Write("model.dsl", Sample);
        _store.GetOrLoad(path);

        // Act.
        _store.Save(path, _store.GetOrLoad(path).Document);

        // Assert.
        Assert.Equal(Sample, File.ReadAllText(path));
    }

    [Fact]
    public void Save_OfAnEditedDocument_ChangesOnlyTheEditedLine()
    {
        // Arrange.
        var path = Write("model.dsl", Sample);
        var document = _store.GetOrLoad(path).Document;
        var line = document.CodeLines.First(l => l.Code.Contains("softwareSystem", StringComparison.Ordinal));
        document.ReplaceLine(line.Number, line.Text.Replace("\"System\"", "\"Renamed\"", StringComparison.Ordinal));

        _store.Save(path, document);

        // Act and assert, step by step.
        var after = File.ReadAllText(path);
        Assert.Contains("Renamed", after, StringComparison.Ordinal);
        Assert.Equal(Sample.Split('\n').Length, after.Split('\n').Length);
    }

    [Fact]
    public void Save_RaisesChanged_OnceTheWorkspaceIsReparsed()
    {
        // Arrange.
        var path = Write("model.dsl", Sample);
        var document = _store.GetOrLoad(path).Document;
        var line = document.CodeLines.First(l => l.Code.Contains("softwareSystem", StringComparison.Ordinal));
        document.ReplaceLine(line.Number, line.Text.Replace("\"System\"", "\"Renamed\"", StringComparison.Ordinal));

        // Act.
        string? raised = null;
        _store.Changed += (_, args) => raised = _store.GetOrLoad(args.Path).Model.Find("s")!.Name;
        _store.Save(path, document);

        // Assert.
        Assert.Equal("Renamed", raised);
    }

    [Fact]
    public void TheModel_ReflectsASave_SoTheNextReaderSeesTheEdit()
    {
        // Arrange and act.
        var path = Write("model.dsl", Sample);
        var document = _store.GetOrLoad(path).Document;
        var line = document.CodeLines.First(l => l.Code.Contains("softwareSystem", StringComparison.Ordinal));
        document.ReplaceLine(line.Number, line.Text.Replace("\"System\"", "\"Renamed\"", StringComparison.Ordinal));
        _store.Save(path, document);

        // Assert.
        Assert.Equal("Renamed", _store.GetOrLoad(path).Model.Find("s")!.Name);
    }

    [Fact]
    public void Save_CreatesTheFolder_WhenTheBodyLivesBesideNothingYet()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "nested", "model.dsl");
        _store.GetOrLoad(path);

        // Act.
        _store.Save(path, _store.GetOrLoad(path).Document);

        // Assert.
        Assert.True(File.Exists(path));
    }
}
