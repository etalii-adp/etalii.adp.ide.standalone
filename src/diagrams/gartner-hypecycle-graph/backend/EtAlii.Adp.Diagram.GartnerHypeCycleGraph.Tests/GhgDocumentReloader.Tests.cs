using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// A deleted body is cleared, and a body that merely could not be read is kept - the pairing the
/// shared lifecycle depends on, and that <see cref="GhgDocumentReloader"/> must forward.
/// </summary>
/// <remarks>
/// <para>
/// <b>Seen to fail with the <c>BodyDeleted</c> override removed.</b> The interface's default then
/// routes a deletion to a reload, the reload finds no file, keeps the last good document, and the
/// deleted diagram stays open with its old elements.
/// </para>
/// <para>
/// <b>The reloader is held as <see cref="IDiagramDocumentReloader"/>, and that is load-bearing.</b> A
/// default interface member is callable only through the interface. Held as the class, removing the
/// override would stop this file compiling instead of making the test fail, so the sabotage could
/// never be seen red. Do not "simplify" the declared type.
/// </para>
/// </remarks>
public sealed class GhgDocumentReloaderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.GhgDocumentReloaderTests", Guid.NewGuid().ToString("N"));

    public GhgDocumentReloaderTests()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(GhgModuleFiles.Example, Body);
    }

    private string Body => Path.Combine(_folder, "technology-trends.ghg");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder left behind is not a test failure.
        }
    }

    [Fact]
    public void AMissingBody_IsKeptOnReload_AndClearedOnlyByTheWatchersDelete()
    {
        // Arrange.
        var store = new GhgDocumentStore();
        IDiagramDocumentReloader reloader = new GhgDocumentReloader(store);
        Assert.NotEmpty(store.GetOrLoad(Body).Model.Trends);
        File.Delete(Body);

        // Act: a reload that finds no file.
        reloader.Reload(_folder, Body);

        // Assert: kept. A body missing on a reload is far more often a publish in flight than a
        // deletion, so the last good document stays.
        Assert.NotEmpty(store.GetOrLoad(Body).Model.Trends);

        // Act: the watcher's evidence that it is gone.
        reloader.BodyDeleted(_folder, Body);

        // Assert: cleared, to what a first open of a missing body would show.
        Assert.Empty(store.GetOrLoad(Body).Model.Trends);
        Assert.Empty(store.GetOrLoad(Body).Model.Influences);
    }

    [Fact]
    public void ADeletion_TellsTheSessions()
    {
        // Arrange.
        var store = new GhgDocumentStore();
        IDiagramDocumentReloader reloader = new GhgDocumentReloader(store);
        store.GetOrLoad(Body);
        var told = new List<string>();
        store.Changed += (_, args) => told.Add(args.Path);
        File.Delete(Body);

        // Act.
        reloader.BodyDeleted(_folder, Body);

        // Assert.
        Assert.Equal([Body], told);
    }
}
