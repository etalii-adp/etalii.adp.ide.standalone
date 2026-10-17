using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// The shared reload seam over the mindmap store, held to the contract every module's is: a body
/// missing on a reload is kept and cleared only by the watcher's delete, a deletion tells the
/// sessions, and a forgotten map is read afresh (backend-centralization R2.4, R2.5).
/// </summary>
public sealed class MindmapDocumentReloaderTests : DocumentReloaderContract<IMindmapDocumentStore>
{
    protected override ToolOrigin Origin => Diagram.Mindmap.Origin;

    protected override string Example => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "architecture.mm");

    protected override IMindmapDocumentStore CreateStore() => new MindmapDocumentStore();

    protected override bool IsEmpty(IMindmapDocumentStore store, string path) => store.GetOrLoad(path).Root.Children.Count == 0;

    protected override void OnChanged(IMindmapDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.BodyPath);

    [Fact]
    public void ADeletedMap_IsClearedToARootNamedAfterTheFile()
    {
        // Arrange.
        using var folder = TestFolder.Create("EtAlii.Adp.MindmapDocumentReloaderTests");
        var body = folder.Combine("design.mm");
        File.Copy(Example, body);
        var store = new MindmapDocumentStore();
        IDiagramDocumentReloader reloader = new DiagramDocumentReloader<IMindmapDocumentStore>(Origin, store);
        Assert.NotEmpty(store.GetOrLoad(body).Root.Children);
        File.Delete(body);

        // Act.
        reloader.BodyDeleted(folder.Path, body);

        // Assert: what a first open of a missing body shows (R2.5).
        var cleared = store.GetOrLoad(body);
        Assert.Empty(cleared.Root.Children);
        Assert.Equal("design", cleared.Root.Text);
    }

    [Fact]
    public void AnUnreadableBody_IsKeptOnReload_AndTellsNobody()
    {
        // Arrange.
        using var folder = TestFolder.Create("EtAlii.Adp.MindmapDocumentReloaderTests");
        var body = folder.Combine("design.mm");
        File.Copy(Example, body);
        var store = new MindmapDocumentStore();
        IDiagramDocumentReloader reloader = new DiagramDocumentReloader<IMindmapDocumentStore>(Origin, store);
        Assert.NotEmpty(store.GetOrLoad(body).Root.Children);
        var told = 0;
        store.Changed += (_, _) => told++;

        // Act: a reload while another program holds the file with no sharing at all.
        using (new FileStream(body, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            reloader.Reload(folder.Path, body);
        }

        // Assert: the last good map is kept, not replaced by an empty one (R2.4), and since nothing a
        // session shows has changed, no session is told.
        Assert.NotEmpty(store.GetOrLoad(body).Root.Children);
        Assert.Equal(0, told);
    }
}
