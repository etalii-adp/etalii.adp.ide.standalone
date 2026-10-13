using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The store over the Databricks family, held to the contract every module's is, and what is its
/// own: byte-identical saves of untouched documents, one file serving all three readings, and broken
/// files refused on save (databricks-diagrams Requirements 2.1 and 2.3).
/// </summary>
public sealed class DatabricksDocumentStoreTests : DocumentStoreContract<IDatabricksDocumentStore, LineDocument, DatabricksModel>
{
    private static string Fixture(string name) => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name);

    protected override string Example => Fixture("bundle.yml");

    protected override string EditMarker => "store-contract-marker";

    protected override IDatabricksDocumentStore CreateStore() => new DatabricksDocumentStore();

    protected override void Edit(LineDocument document) =>
        BundleWriter.SetName(document, DatabricksModel.Read(document).Bundle, EditMarker);

    protected override string TextOf(LineDocument document) => document.Text;

    protected override bool IsEmpty(DatabricksModel model) =>
        model.Bundle.Name == "" && model.Jobs.Count == 0 && model.Pipelines.Count == 0;

    [Theory]
    [InlineData("bundle.yml")]
    [InlineData("job.yml")]
    [InlineData("pipeline.json")]
    [InlineData("crlf-line-endings.yml")]
    [InlineData("lf-line-endings.yml")]
    [InlineData("no-trailing-newline.yml")]
    [InlineData("tied-line-endings.yml")]
    public void SavingAnUntouchedDocument_LeavesTheFileByteIdentical(string name)
    {
        // Arrange.
        using var folder = TestFolder.Create("databricks-store");
        var path = folder.Combine(name);
        File.Copy(Fixture(name), path);
        var original = File.ReadAllBytes(path);
        var store = new DatabricksDocumentStore();
        var entry = store.GetOrLoad(path);

        // Act. Nothing is edited between the load and the save, which is what makes this test about
        // byte identity rather than about edits surviving - see the LostEdit test beside it for the
        // case an untouched document cannot express.
        var saved = store.Save(path, entry.Document);

        // Assert.
        Assert.Equal("", saved.Error);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void AMissingFile_OpensAsAnEmptyUsableDocument()
    {
        // Arrange.
        using var folder = TestFolder.Create("databricks-store");

        // Act.
        var entry = new DatabricksDocumentStore().GetOrLoad(folder.Combine("not-yet.yml"));

        // Assert: a freshly created diagram opens, and the first save creates the file.
        Assert.True(entry.IsUsable);
        Assert.Empty(entry.Document.Lines);
        Assert.Empty(entry.Model.Jobs);
    }

    [Fact]
    public void AFileThatIsNotYaml_IsHeldUnreadable_AndASaveIsRefusedUnwritten()
    {
        // Arrange.
        using var folder = TestFolder.Create("databricks-store");
        var path = folder.Combine("broken.yml");
        File.Copy(Fixture("broken.yml"), path);
        var before = File.ReadAllBytes(path);
        var store = new DatabricksDocumentStore();

        // Act.
        var entry = store.GetOrLoad(path);
        var saved = store.Save(path, entry.Document);

        // Assert: the broken file is never made worse - the save is refused with the reason.
        Assert.False(entry.IsUsable);
        Assert.NotEqual("", entry.Unreadable);
        Assert.True(saved.Failed);
        Assert.Contains("could not be read", saved.Error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void OneFile_ServesAllThreeReadings()
    {
        // Arrange & act.
        var entry = new DatabricksDocumentStore().GetOrLoad(Example);

        // Assert: a databricks.yml is a bundle that also declares a job and a pipeline inline - one
        // entry serves whichever diagram type opens it.
        Assert.Equal("lakehouse-nightly", entry.Model.Bundle.Name);
        Assert.Equal("nightly_ingest", Assert.Single(entry.Model.Jobs).Key);
        Assert.Equal("bronze_to_gold", Assert.Single(entry.Model.Pipelines).Key);
    }

    [Fact]
    public void Reload_PicksUpAnExternalChange_AndSaysSo()
    {
        // Arrange.
        using var folder = TestFolder.Create("databricks-store");
        var path = folder.Combine("crlf-line-endings.yml");
        File.Copy(Fixture("crlf-line-endings.yml"), path);
        var store = new DatabricksDocumentStore();
        store.GetOrLoad(path);
        File.WriteAllText(path, "bundle:\r\n  name: replaced\r\n");
        string? heard = null;
        store.Changed += (_, args) => heard = args.Path;

        // Act.
        store.Reload(path);

        // Assert.
        Assert.Equal(path, heard);
        Assert.Equal("replaced", store.GetOrLoad(path).Model.Bundle.Name);
    }
}
