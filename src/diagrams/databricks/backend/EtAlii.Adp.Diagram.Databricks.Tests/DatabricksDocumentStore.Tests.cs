using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
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

    protected override IDatabricksDocumentStore CreateStore() => new DatabricksDocumentStore(new FileWriter());

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
    public async Task SavingAnUntouchedDocument_LeavesTheFileByteIdentical(string name)
    {
        // Arrange.
        using var folder = TestFolder.Create("databricks-store");
        var path = folder.Combine(name);
        File.Copy(Fixture(name), path);
        var original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var store = new DatabricksDocumentStore(new FileWriter());
        var entry = await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Act. Nothing is edited between the load and the save, which is what makes this test about
        // byte identity rather than about edits surviving - see the LostEdit test beside it for the
        // case an untouched document cannot express.
        var saved = await store.SaveAsync(path, entry.Document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("", saved.Error);
        Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMissingFile_OpensAsAnEmptyUsableDocument()
    {
        // Arrange.
        using var folder = TestFolder.Create("databricks-store");

        // Act.
        var entry = await new DatabricksDocumentStore(new FileWriter()).GetOrLoadAsync(folder.Combine("not-yet.yml"), TestContext.Current.CancellationToken);

        // Assert: a freshly created diagram opens, and the first save creates the file.
        Assert.True(entry.IsUsable);
        Assert.Empty(entry.Document.Lines);
        Assert.Empty(entry.Model.Jobs);
    }

    [Fact]
    public async Task AFileThatIsNotYaml_IsHeldUnreadable_AndASaveIsRefusedUnwritten()
    {
        // Arrange.
        using var folder = TestFolder.Create("databricks-store");
        var path = folder.Combine("broken.yml");
        File.Copy(Fixture("broken.yml"), path);
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var store = new DatabricksDocumentStore(new FileWriter());

        // Act.
        var entry = await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        var saved = await store.SaveAsync(path, entry.Document, TestContext.Current.CancellationToken);

        // Assert: the broken file is never made worse - the save is refused with the reason.
        Assert.False(entry.IsUsable);
        Assert.NotEqual("", entry.Unreadable);
        var problem = Assert.Single(entry.Problems);
        Assert.Equal((1, entry.Unreadable), (problem.Line, problem.Message));
        Assert.True(saved.Failed);
        Assert.Contains("could not be read", saved.Error, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OneFile_ServesAllThreeReadings()
    {
        // Arrange & act.
        var entry = await new DatabricksDocumentStore(new FileWriter()).GetOrLoadAsync(Example, TestContext.Current.CancellationToken);

        // Assert: a databricks.yml is a bundle that also declares a job and a pipeline inline - one
        // entry serves whichever diagram type opens it.
        Assert.Equal("lakehouse-nightly", entry.Model.Bundle.Name);
        Assert.Equal("nightly_ingest", Assert.Single(entry.Model.Jobs).Key);
        Assert.Equal("bronze_to_gold", Assert.Single(entry.Model.Pipelines).Key);
    }

    [Fact]
    public async Task Reload_PicksUpAnExternalChange_AndSaysSo()
    {
        // Arrange.
        using var folder = TestFolder.Create("databricks-store");
        var path = folder.Combine("crlf-line-endings.yml");
        File.Copy(Fixture("crlf-line-endings.yml"), path);
        var store = new DatabricksDocumentStore(new FileWriter());
        await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, "bundle:\r\n  name: replaced\r\n", TestContext.Current.CancellationToken);
        string? heard = null;
        store.Changed += (_, args) => heard = args.Path;

        // Act.
        await store.ReloadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(path, heard);
        Assert.Equal("replaced", (await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken)).Model.Bundle.Name);
    }
}
