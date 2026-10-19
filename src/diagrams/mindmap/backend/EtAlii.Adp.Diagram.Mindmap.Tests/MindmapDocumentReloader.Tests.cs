using EtAlii.Adp.Files;
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

    protected override IMindmapDocumentStore CreateStore() => new MindmapDocumentStore(new FileWriter());

    protected override async Task<bool> IsEmptyAsync(IMindmapDocumentStore store, string path, CancellationToken cancellationToken) => (await store.GetOrLoadAsync(path, cancellationToken)).Root.Children.Count == 0;

    protected override void OnChanged(IMindmapDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.BodyPath);

    [Fact]
    public async Task ADeletedMap_IsClearedToARootNamedAfterTheFile()
    {
        // Arrange.
        using var folder = TestFolder.Create("EtAlii.Adp.MindmapDocumentReloaderTests");
        var body = folder.Combine("design.mm");
        File.Copy(Example, body);
        var store = new MindmapDocumentStore(new FileWriter());
        IDiagramDocumentReloader reloader = new DiagramDocumentReloader<IMindmapDocumentStore>(Origin, store);
        Assert.NotEmpty((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Root.Children);
        File.Delete(body);

        // Act.
        await reloader.BodyDeletedAsync(ProjectRoot.From(folder.Path), body, TestContext.Current.CancellationToken);

        // Assert: what a first open of a missing body shows (R2.5).
        var cleared = await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken);
        Assert.Empty(cleared.Root.Children);
        Assert.Equal("design", cleared.Root.Text);
    }

    [Fact]
    public async Task AnUnreadableBody_IsKeptOnReload_AndTellsNobody()
    {
        // Arrange.
        using var folder = TestFolder.Create("EtAlii.Adp.MindmapDocumentReloaderTests");
        var body = folder.Combine("design.mm");
        File.Copy(Example, body);
        var store = new MindmapDocumentStore(new FileWriter());
        IDiagramDocumentReloader reloader = new DiagramDocumentReloader<IMindmapDocumentStore>(Origin, store);
        Assert.NotEmpty((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Root.Children);
        var told = 0;
        store.Changed += (_, _) => told++;

        // Act: a reload while another program holds the file with no sharing at all.
        await using (new FileStream(body, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await reloader.ReloadAsync(ProjectRoot.From(folder.Path), body, TestContext.Current.CancellationToken);
        }

        // Assert: the last good map is kept, not replaced by an empty one (R2.4), and since nothing a
        // session shows has changed, no session is told.
        Assert.NotEmpty((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).Root.Children);
        Assert.Equal(0, told);
    }
}
