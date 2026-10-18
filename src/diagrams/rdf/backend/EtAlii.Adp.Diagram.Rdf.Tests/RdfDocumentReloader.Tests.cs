using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The shared reload seam over the family's one store, held to the contract every module's is: a
/// body missing on a reload is kept, a deleted one is cleared, and the sessions hear of both
/// (backend-centralization R2.4, R2.5).
/// </summary>
public sealed class RdfDocumentReloaderTests : DocumentReloaderContract<IRdfDocumentStore>
{
    protected override ToolOrigin Origin => ServiceCollectionAddRdfExtension.RdfOrigin;

    protected override string Example => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "constructs.ttl");

    protected override IRdfDocumentStore CreateStore() => new RdfDocumentStore(new FileWriter());

    protected override bool IsEmpty(IRdfDocumentStore store, string path) => store.GetOrLoad(path).Model.Triples.Count == 0;

    protected override void OnChanged(IRdfDocumentStore store, Action<string> changed) =>
        store.Changed += (_, args) => changed(args.Path);

    [Fact]
    public async Task AnUnreadableBody_IsKeptOnReload_AndTellsNobody()
    {
        // Arrange.
        var store = CreateStore();
        IDiagramDocumentReloader reloader = new DiagramDocumentReloader<IRdfDocumentStore>(Origin, store);
        using var folder = TestFolder.Create("EtAlii.Adp.RdfDocumentReloaderTests");
        var body = folder.Combine("constructs.ttl");
        File.Copy(Example, body);
        Assert.False(IsEmpty(store, body));
        var told = 0;
        store.Changed += (_, _) => told++;

        // Act: a reload while another program holds the file with no sharing at all.
        await using (new FileStream(body, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await reloader.ReloadAsync(folder.Path, body, TestContext.Current.CancellationToken);
        }

        // Assert: the last good document is kept, not replaced by an empty one (R2.4), and since
        // nothing a session shows has changed, no session is told.
        Assert.False(IsEmpty(store, body));
        Assert.Equal(0, told);
    }

    [Fact]
    public async Task EveryReadingOfTheFamily_ReloadsThroughTheOneStore()
    {
        // Arrange: a body each reading could have opened, and the family registered as the host does.
        var store = CreateStore();
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddRdf();
        using var provider = services.BuildServiceProvider();
        using var folder = TestFolder.Create("EtAlii.Adp.RdfDocumentReloaderTests");
        var body = folder.Combine("constructs.ttl");
        File.Copy(Example, body);
        store.GetOrLoad(body);
        var reloaders = provider.GetServices<IDiagramDocumentReloader>().ToList();

        // Act & assert: each reading's reload reaches the store, so a session of any of them sees an
        // outside write. A reading left without one would show its last reading until reopened.
        string[] readings = ["rdf", "owl", "skos", "shacl"];
        foreach (var reading in readings)
        {
            var told = new List<string>();
            void Told(object? sender, DocumentChangedEventArgs args) => told.Add(args.Path);
            store.Changed += Told;
            var reloader = Assert.Single(reloaders, r => r.Origin == new ToolOrigin("w3c", reading));
            await File.AppendAllTextAsync(body, $"<http://example.org/{reading}> <http://example.org/y> <http://example.org/z> .\r\n", TestContext.Current.CancellationToken);
            await reloader.ReloadAsync(folder.Path, body, TestContext.Current.CancellationToken);
            store.Changed -= Told;
            Assert.Equal([body], told);
        }
    }
}
