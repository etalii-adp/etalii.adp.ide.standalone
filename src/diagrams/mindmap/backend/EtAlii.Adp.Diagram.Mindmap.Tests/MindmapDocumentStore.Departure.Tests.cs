using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

/// <summary>
/// The two ways mindmap's store deliberately differs from the other eight writable stores, each
/// permitted by backend-centralization R2.8 and pinned here (task 10).
/// </summary>
/// <remarks>
/// <para>
/// <b>These are DELIBERATE DEPARTURES, not inconsistencies left over from the conversion.</b> A later
/// reader bringing mindmap "in line" with the other stores will make one of these tests fail, and
/// that is the point: R2.8 says mindmap MAY keep both, and the design says they are declared rather
/// than tolerated. Removing either is a requirements change, not a tidy-up.
/// </para>
/// <para>
/// The reloader is held as <see cref="IDiagramDocumentReloader"/>, as the host holds it: a default
/// interface member is callable only through the interface, so the deletion is seen to reach the
/// store through the shared reloader's own <c>BodyDeleted</c> rather than the default reload.
/// </para>
/// </remarks>
public sealed class MindmapDocumentStoreDepartureTests : IDisposable
{
    private const string Map = """
        <map version="freeplane 1.11.5">
          <node TEXT="Design" ID="ID_1">
            <node TEXT="Layout" ID="ID_2"/>
          </node>
        </map>
        """;

    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.MindmapDocumentStoreDepartureTests", Guid.NewGuid().ToString("N"));

    public MindmapDocumentStoreDepartureTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Body, Map);
    }

    private string Body => IoPath.Combine(_folder, "design.mm");

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
    }

    [Fact]
    public void PermittedDepartureR28_AReloadOrDeletionOfAMapNeverLoaded_IsSkipped()
    {
        // Arrange: a map on disk that nothing has opened.
        var store = new MindmapDocumentStore(new FileWriter());
        IDiagramDocumentReloader reloader = new DiagramDocumentReloader<IMindmapDocumentStore>(Diagram.Mindmap.Origin, store);
        var told = 0;
        store.Changed += (_, _) => told++;

        // Act: the watcher reports a change to it.
        reloader.Reload(_folder, Body);

        // Assert: skipped. The shared lifecycle would open it and the store would announce it; this
        // store leaves a map nobody has open unopened, and tells nobody.
        Assert.Null(store.Get(Body));
        Assert.Equal(0, told);

        // Act: and the watcher reports it deleted.
        File.Delete(Body);
        reloader.BodyDeleted(_folder, Body);

        // Assert: skipped as well - no empty map is installed for a path nobody opened.
        Assert.Null(store.Get(Body));
        Assert.Equal(0, told);
    }

    [Fact]
    public async Task PermittedDepartureR28_TheChangeEventNamesTheKindOfStructuralChange()
    {
        // Arrange.
        var store = new MindmapDocumentStore(new FileWriter());
        IDiagramDocumentReloader reloader = new DiagramDocumentReloader<IMindmapDocumentStore>(Diagram.Mindmap.Origin, store);
        var document = store.GetOrLoad(Body);
        var told = new List<MindmapChange>();
        store.Changed += (_, args) => told.Add(args.Change);

        // Act: a command's edit and save, then an external edit picked up, then a deletion.
        document.SetText(document.Find("ID_2")!, "Layout, renamed");
        var saved = await store.SaveAsync(Body, document, new MindmapNodeUpdated("ID_2"), TestContext.Current.CancellationToken);
        Assert.False(saved.Failed);
        await File.WriteAllTextAsync(Body, Map, TestContext.Current.CancellationToken);
        reloader.Reload(_folder, Body);
        File.Delete(Body);
        reloader.BodyDeleted(_folder, Body);

        // Assert: a save announces exactly the structural change the command made, so a session can
        // lay out only what moved; a reload and a deletion, after which anything may differ, announce
        // MindmapReloaded. Not the path-and-model shape the other stores raise.
        Assert.Collection(
            told,
            change => Assert.Equal(new MindmapNodeUpdated("ID_2"), change),
            change => Assert.IsType<MindmapReloaded>(change),
            change => Assert.IsType<MindmapReloaded>(change));
    }
}
