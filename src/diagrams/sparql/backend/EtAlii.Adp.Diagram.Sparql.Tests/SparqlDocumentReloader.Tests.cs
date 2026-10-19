using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// The shared reload seam over the read-only query store, held to the contract every module's is:
/// a query missing on a reload is kept, a deleted one is cleared, and the sessions hear of both
/// (backend-centralization R2.4, R2.5).
/// </summary>
public sealed class SparqlDocumentReloaderTests : DocumentReloaderContract<ISparqlDocumentStore>
{
    protected override ToolOrigin Origin => ServiceCollectionAddSparqlExtension.SparqlOrigin;

    protected override string Example => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "lf-line-endings.rq");

    protected override ISparqlDocumentStore CreateStore() => new SparqlDocumentStore();

    protected override async Task<bool> IsEmptyAsync(ISparqlDocumentStore store, string path, CancellationToken cancellationToken) => ReferenceEquals((await store.GetOrLoadAsync(path, cancellationToken)).Model, SparqlQueryModel.Empty);

    protected override void OnChanged(ISparqlDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);

    [Fact]
    public async Task ADeletedQuery_OpensAsTheMissingOneDoes_NamingTheTextEditor()
    {
        // Arrange.
        var store = CreateStore();
        IDiagramDocumentReloader reloader = new DiagramDocumentReloader<ISparqlDocumentStore>(Origin, store);
        using var folder = TestFolder.Create("EtAlii.Adp.SparqlDocumentReloaderTests");
        var body = folder.Combine("query.rq");
        File.Copy(Example, body);
        Assert.True((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).IsUsable);
        File.Delete(body);

        // Act.
        await reloader.BodyDeletedAsync(ProjectRoot.From(folder.Path), body, TestContext.Current.CancellationToken);

        // Assert: this module never creates query files, so a gone one is a state it names.
        var cleared = await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken);
        Assert.False(cleared.IsUsable);
        Assert.Contains("does not exist", cleared.Unreadable, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreadableBody_IsKeptOnReload_AndTellsNobody()
    {
        // Arrange.
        var store = CreateStore();
        IDiagramDocumentReloader reloader = new DiagramDocumentReloader<ISparqlDocumentStore>(Origin, store);
        using var folder = TestFolder.Create("EtAlii.Adp.SparqlDocumentReloaderTests");
        var body = folder.Combine("query.rq");
        File.Copy(Example, body);
        Assert.True((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).IsUsable);
        var told = 0;
        store.Changed += (_, _) => told++;

        // Act: a reload while another program holds the file with no sharing at all.
        await using (new FileStream(body, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await reloader.ReloadAsync(ProjectRoot.From(folder.Path), body, TestContext.Current.CancellationToken);
        }

        // Assert: the last good query is kept, not replaced by one naming the read failure (R2.4),
        // and since nothing a session shows has changed, no session is told.
        Assert.True((await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken)).IsUsable);
        Assert.Equal(0, told);
    }
}
