using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// A query that could not be read is kept, and a deleted query is cleared - the pairing the shared
/// lifecycle depends on (backend-centralization R2.4, R2.5), which task 7 brings to this read-only
/// store in one change and which <see cref="SparqlDocumentReloader"/> must forward.
/// </summary>
/// <remarks>
/// <b>The reloader is held as <see cref="IDiagramDocumentReloader"/>, and that is load-bearing.</b> A
/// default interface member is callable only through the interface. Held as the class, removing the
/// <c>BodyDeleted</c> override would stop this file compiling instead of making a test fail, so the
/// defect could never be seen red. Do not "simplify" the declared type.
/// </remarks>
public sealed class SparqlDocumentReloaderTests : IDisposable
{
    private const string Query = """
        PREFIX ex: <http://example.org/>
        SELECT ?s
        WHERE { ?s ex:p ?o }
        """;

    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.SparqlDocumentReloaderTests", Guid.NewGuid().ToString("N"));

    public SparqlDocumentReloaderTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Body, Query);
    }

    private string Body => IoPath.Combine(_folder, "query.rq");

    public void Dispose()
    {
        TestFolder.TryDelete(_folder);
    }

    private static IDiagramDocumentReloader ReloaderFor(ISparqlDocumentStore store) =>
        new SparqlDocumentReloader(ServiceCollectionAddSparqlExtension.SparqlOrigin, store);

    [Fact]
    public void AMissingBody_IsKeptOnReload_AndClearedOnlyByTheWatchersDelete()
    {
        // Arrange.
        var store = new SparqlDocumentStore();
        var reloader = ReloaderFor(store);
        Assert.True(store.GetOrLoad(Body).IsUsable);
        File.Delete(Body);

        // Act: a reload that finds no file.
        reloader.Reload(_folder, Body);

        // Assert: kept until the absence is confirmed (R2.5's first half). A query missing on a
        // reload is far more often a publish in flight than a deletion, so the last good query
        // stays. This is NOT R2.4, which is a body present but unreadable - see the next test.
        Assert.True(store.GetOrLoad(Body).IsUsable);

        // Act: the watcher's evidence that it is gone.
        reloader.BodyDeleted(_folder, Body);

        // Assert: cleared, to what a first open of a missing query shows (R2.5).
        var cleared = store.GetOrLoad(Body);
        Assert.False(cleared.IsUsable);
        Assert.Contains("does not exist", cleared.Error);
    }

    [Fact]
    public void AnUnreadableBody_IsKeptOnReload_AndTellsNobody()
    {
        // Arrange.
        var store = new SparqlDocumentStore();
        var reloader = ReloaderFor(store);
        Assert.True(store.GetOrLoad(Body).IsUsable);
        var told = 0;
        store.Changed += (_, _) => told++;

        // Act: a reload while another program holds the file with no sharing at all.
        using (new FileStream(Body, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            reloader.Reload(_folder, Body);
        }

        // Assert: the last good query is kept, not replaced by one naming the read failure (R2.4),
        // and since nothing a session shows has changed, no session is told.
        Assert.True(store.GetOrLoad(Body).IsUsable);
        Assert.Equal(0, told);
    }

    [Fact]
    public void ADeletion_TellsTheSessions()
    {
        // Arrange.
        var store = new SparqlDocumentStore();
        var reloader = ReloaderFor(store);
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
