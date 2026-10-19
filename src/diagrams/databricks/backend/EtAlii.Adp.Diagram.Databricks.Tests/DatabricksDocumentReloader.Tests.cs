using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The shared reload seam over the Databricks store, held to the contract every module's is: a body
/// missing on a reload is kept and cleared only by the watcher's delete, a deletion tells the
/// sessions, and an unreadable body is kept (backend-centralization R2.4, R2.5).
/// </summary>
public sealed class DatabricksDocumentReloaderTests : DocumentReloaderContract<IDatabricksDocumentStore>
{
    protected override ToolOrigin Origin => ServiceCollectionAddDatabricksExtension.BundleOrigin;

    protected override string Example => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "bundle.yml");

    protected override IDatabricksDocumentStore CreateStore() => new DatabricksDocumentStore(new FileWriter());

    protected override async Task<bool> IsEmptyAsync(IDatabricksDocumentStore store, string path, CancellationToken cancellationToken) => (await store.GetOrLoadAsync(path, cancellationToken)).Model.Bundle.Name == "";

    protected override void OnChanged(IDatabricksDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);

    [Theory]
    [InlineData("bundle")]
    [InlineData("job")]
    [InlineData("pipeline")]
    public async Task EachTypeOfTheFamily_ReloadsIntoTheOneSharedStore(string type)
    {
        // Arrange.
        await using var provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddDatabricks()
            .BuildServiceProvider();
        using var folder = TestFolder.Create("EtAlii.Adp.DatabricksDocumentReloaderTests");
        var body = folder.Combine("databricks.yml");
        File.Copy(Example, body);
        var store = provider.GetRequiredService<IDatabricksDocumentStore>();
        await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(body, "bundle:\r\n  name: replaced\r\n", TestContext.Current.CancellationToken);
        var reloader = provider.GetServices<IDiagramDocumentReloader>().Single(candidate => candidate.Origin == new ToolOrigin("databricks", type));

        // Act.
        await reloader.ReloadAsync(folder.Path, body, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("replaced", (await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Model.Bundle.Name);
    }

    [Fact]
    public async Task AnUnreadableBody_IsKeptOnReload_AndTellsNobody()
    {
        // Arrange.
        using var folder = TestFolder.Create("EtAlii.Adp.DatabricksDocumentReloaderTests");
        var body = folder.Combine("databricks.yml");
        File.Copy(Example, body);
        var store = new DatabricksDocumentStore(new FileWriter());
        IDiagramDocumentReloader reloader = new DiagramDocumentReloader<IDatabricksDocumentStore>(Origin, store);
        Assert.Equal("lakehouse-nightly", (await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Model.Bundle.Name);
        var told = 0;
        store.Changed += (_, _) => told++;

        // Act: a reload while another program holds the file with no sharing at all.
        await using (new FileStream(body, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await reloader.ReloadAsync(folder.Path, body, TestContext.Current.CancellationToken);
        }

        // Assert: the last good bundle is kept, not replaced by an empty one (R2.4), and since
        // nothing a session shows has changed, no session is told.
        Assert.Equal("lakehouse-nightly", (await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Model.Bundle.Name);
        Assert.Equal(0, told);
    }
}
