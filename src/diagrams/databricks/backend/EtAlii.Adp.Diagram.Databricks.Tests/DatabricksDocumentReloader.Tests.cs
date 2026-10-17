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

    protected override IDatabricksDocumentStore CreateStore() => new DatabricksDocumentStore();

    protected override bool IsEmpty(IDatabricksDocumentStore store, string path) => store.GetOrLoad(path).Model.Bundle.Name == "";

    protected override void OnChanged(IDatabricksDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);

    [Theory]
    [InlineData("bundle")]
    [InlineData("job")]
    [InlineData("pipeline")]
    public void EachTypeOfTheFamily_ReloadsIntoTheOneSharedStore(string type)
    {
        // Arrange.
        using var provider = new ServiceCollection()
            .AddSingleton<IReadOnlyList<DiagramDefinition>>(Diagram.Definitions)
            .AddCommands().AddHierarchyCommandHandlers()
            .AddDatabricks()
            .BuildServiceProvider();
        using var folder = TestFolder.Create("EtAlii.Adp.DatabricksDocumentReloaderTests");
        var body = folder.Combine("databricks.yml");
        File.Copy(Example, body);
        var store = provider.GetRequiredService<IDatabricksDocumentStore>();
        store.GetOrLoad(body);
        File.WriteAllText(body, "bundle:\r\n  name: replaced\r\n");
        var reloader = provider.GetServices<IDiagramDocumentReloader>().Single(candidate => candidate.Origin == new ToolOrigin("databricks", type));

        // Act.
        reloader.Reload(folder.Path, body);

        // Assert.
        Assert.Equal("replaced", store.GetOrLoad(body).Model.Bundle.Name);
    }

    [Fact]
    public void AnUnreadableBody_IsKeptOnReload_AndTellsNobody()
    {
        // Arrange.
        using var folder = TestFolder.Create("EtAlii.Adp.DatabricksDocumentReloaderTests");
        var body = folder.Combine("databricks.yml");
        File.Copy(Example, body);
        var store = new DatabricksDocumentStore();
        IDiagramDocumentReloader reloader = new DiagramDocumentReloader<IDatabricksDocumentStore>(Origin, store);
        Assert.Equal("lakehouse-nightly", store.GetOrLoad(body).Model.Bundle.Name);
        var told = 0;
        store.Changed += (_, _) => told++;

        // Act: a reload while another program holds the file with no sharing at all.
        using (new FileStream(body, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            reloader.Reload(folder.Path, body);
        }

        // Assert: the last good bundle is kept, not replaced by an empty one (R2.4), and since
        // nothing a session shows has changed, no session is told.
        Assert.Equal("lakehouse-nightly", store.GetOrLoad(body).Model.Bundle.Name);
        Assert.Equal(0, told);
    }
}
