using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The store's lifecycle: load-once, byte-identical saves of untouched documents, errors carried
/// rather than thrown, and broken files refused on save (databricks-diagrams Requirements 2.1
/// and 2.3).
/// </summary>
public class DatabricksDocumentStoreTests : IDisposable
{
    private readonly string _root;
    private readonly DatabricksDocumentStore _store = new();

    public DatabricksDocumentStoreTests()
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
    [InlineData("bundle.yml")]
    [InlineData("job.yml")]
    [InlineData("pipeline.json")]
    [InlineData("crlf-line-endings.yml")]
    [InlineData("lf-line-endings.yml")]
    [InlineData("no-trailing-newline.yml")]
    public void SavingAnUntouchedDocument_LeavesTheFileByteIdentical(string name)
    {
        // Arrange.
        var path = CopyFixture(name);
        var original = File.ReadAllBytes(path);
        _store.GetOrLoad(path);

        // Act.
        var refusal = _store.Save(path);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void AMissingFile_OpensAsAnEmptyUsableDocument()
    {
        // Arrange & act.
        var entry = _store.GetOrLoad(IoPath.Combine(_root, "not-yet.yml"));

        // Assert.
        // A freshly created diagram opens, and the first save creates the file.
        Assert.True(entry.IsUsable);
        Assert.Empty(entry.Document.Lines);
        Assert.Empty(entry.Jobs);
    }

    [Fact]
    public void AFileThatIsNotYaml_CarriesTheErrorAndItsLine_AndSaveRefuses()
    {
        // Arrange.
        var path = CopyFixture("broken.yml");

        // Act.
        var entry = _store.GetOrLoad(path);
        var refusal = _store.Save(path);

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.NotEqual("", entry.Error);
        Assert.True(entry.ErrorLine >= 1);
        // The broken file is never made worse: the save is refused with the reason.
        Assert.Contains("does not parse", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void OneFile_ServesAllThreeReadings()
    {
        // Arrange.
        var path = CopyFixture("bundle.yml");

        // Act.
        var entry = _store.GetOrLoad(path);

        // Assert.
        // A databricks.yml is a bundle that also declares a job and a pipeline inline - one
        // entry serves whichever diagram type opens it.
        Assert.Equal("lakehouse-nightly", entry.Bundle.Name);
        Assert.Equal("nightly_ingest", Assert.Single(entry.Jobs).Key);
        Assert.Equal("bronze_to_gold", Assert.Single(entry.Pipelines).Key);
    }

    [Fact]
    public void Reload_PicksUpAnExternalChange_AndSaysSo()
    {
        // Arrange.
        var path = CopyFixture("crlf-line-endings.yml");
        _store.GetOrLoad(path);
        File.WriteAllText(path, "bundle:\r\n  name: replaced\r\n");
        DatabricksDocumentChangedEventArgs? heard = null;
        _store.Changed += (_, args) => heard = args;

        // Act.
        _store.Reload(path);

        // Assert.
        Assert.NotNull(heard);
        Assert.Equal(path, heard.Path);
        Assert.Equal("replaced", heard.Entry.Bundle.Name);
        Assert.Equal("replaced", _store.GetOrLoad(path).Bundle.Name);
    }
}
