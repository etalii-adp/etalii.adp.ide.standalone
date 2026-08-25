using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.C4.Tests;

/// <summary>
/// The store that lets several diagrams share one document, which is the mechanism behind
/// "model once, view many" (c4-diagrams Requirement 1.2).
/// </summary>
public class C4DocumentStoreTests : IDisposable
{
    private readonly string _root;
    private readonly C4DocumentStore _store = new();

    public C4DocumentStoreTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
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
        Assert.Same(_store.GetOrLoad(path), _store.GetOrLoad(path));
    }

    [Fact]
    public void AMissingFile_OpensAsAnEmptyDocument_RatherThanThrowing()
    {
        // Act.
        var document = _store.GetOrLoad(IoPath.Combine(_root, "not-written-yet.dsl"));

        // Assert.
        Assert.Empty(document.Lines.Where(line => line.Length > 0));
        Assert.Empty(_store.WorkspaceOf(IoPath.Combine(_root, "not-written-yet.dsl")).Elements);
    }

    [Fact]
    public void Save_WritesTheDocumentBack_ByteForByte()
    {
        // Arrange.
        var path = Write("model.dsl", Sample);
        _store.GetOrLoad(path);

        // Act.
        _store.Save(path);

        // Assert.
        Assert.Equal(Sample, File.ReadAllText(path));
    }

    [Fact]
    public void Save_OfAnEditedDocument_ChangesOnlyTheEditedLine()
    {
        // Arrange.
        var path = Write("model.dsl", Sample);
        var document = _store.GetOrLoad(path);
        var line = document.CodeLines.First(l => l.Code.Contains("softwareSystem", StringComparison.Ordinal));
        document.ReplaceLine(line.Number, line.Text.Replace("\"System\"", "\"Renamed\"", StringComparison.Ordinal));

        _store.Save(path);

        // Act and assert, step by step.
        var after = File.ReadAllText(path);
        Assert.Contains("Renamed", after, StringComparison.Ordinal);
        Assert.Equal(Sample.Split('\n').Length, after.Split('\n').Length);
    }

    [Fact]
    public void Save_RaisesChanged_WithTheReparsedWorkspace()
    {
        // Arrange.
        var path = Write("model.dsl", Sample);
        var document = _store.GetOrLoad(path);
        var line = document.CodeLines.First(l => l.Code.Contains("softwareSystem", StringComparison.Ordinal));
        document.ReplaceLine(line.Number, line.Text.Replace("\"System\"", "\"Renamed\"", StringComparison.Ordinal));

        // Act.
        C4DocumentChangedEventArgs? raised = null;
        _store.Changed += (_, args) => raised = args;
        _store.Save(path);

        // Assert.
        Assert.NotNull(raised);
        Assert.Equal(path, raised!.Path);
        Assert.Equal("Renamed", raised.Workspace.Find("s")!.Name);
    }

    [Fact]
    public void WorkspaceOf_ReflectsASave_SoTheNextReaderSeesTheEdit()
    {
        // Arrange and act.
        var path = Write("model.dsl", Sample);
        var document = _store.GetOrLoad(path);
        var line = document.CodeLines.First(l => l.Code.Contains("softwareSystem", StringComparison.Ordinal));
        document.ReplaceLine(line.Number, line.Text.Replace("\"System\"", "\"Renamed\"", StringComparison.Ordinal));
        _store.Save(path);

        // Assert.
        Assert.Equal("Renamed", _store.WorkspaceOf(path).Find("s")!.Name);
    }

    [Fact]
    public void Reload_PicksUpAnExternalEdit_AndTellsTheSessions()
    {
        // Arrange.
        var path = Write("model.dsl", Sample);
        _store.GetOrLoad(path);
        File.WriteAllText(path, Sample.Replace("\"System\"", "\"Edited elsewhere\"", StringComparison.Ordinal));

        // Act.
        C4DocumentChangedEventArgs? raised = null;
        _store.Changed += (_, args) => raised = args;
        _store.Reload(path);

        // Assert.
        Assert.NotNull(raised);
        Assert.Equal("Edited elsewhere", _store.WorkspaceOf(path).Find("s")!.Name);
    }

    [Fact]
    public void Forget_MakesTheNextOpenReadTheFileAgain()
    {
        // Arrange.
        var path = Write("model.dsl", Sample);
        var first = _store.GetOrLoad(path);

        // Act.
        _store.Forget(path);

        // Assert.
        Assert.NotSame(first, _store.GetOrLoad(path));
    }

    [Fact]
    public void Save_CreatesTheFolder_WhenTheBodyLivesBesideNothingYet()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "nested", "model.dsl");
        _store.GetOrLoad(path);

        // Act.
        _store.Save(path);

        // Assert.
        Assert.True(File.Exists(path));
    }
}
