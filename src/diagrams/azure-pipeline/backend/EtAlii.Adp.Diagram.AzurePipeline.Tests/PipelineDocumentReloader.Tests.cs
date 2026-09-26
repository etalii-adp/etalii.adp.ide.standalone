using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// A body that could not be read is kept, and a deleted body is cleared - the pairing the shared
/// lifecycle depends on (backend-centralization R2.4, R2.5), which task 6 brings to this store in
/// one change and which <see cref="PipelineDocumentReloader"/> must forward.
/// </summary>
/// <remarks>
/// <b>The reloader is held as <see cref="IDiagramDocumentReloader"/>, and that is load-bearing.</b> A
/// default interface member is callable only through the interface. Held as the class, removing the
/// <c>BodyDeleted</c> override would stop this file compiling instead of making a test fail, so the
/// defect could never be seen red. Do not "simplify" the declared type.
/// </remarks>
public sealed class PipelineDocumentReloaderTests : IDisposable
{
    private readonly string _root = IoPath.Combine(IoPath.GetTempPath(), "adp-pipeline-reloader-" + Guid.NewGuid().ToString("N"));

    public PipelineDocumentReloaderTests()
    {
        Directory.CreateDirectory(_root);

        // A real pipeline of this notation: two jobs under the implicit stage.
        File.Copy(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "jobs-only.yml"), Body);
    }

    private string Body => IoPath.Combine(_root, "azure-pipelines.yml");

    public void Dispose() => TestFolder.TryDelete(_root);

    [Fact]
    public void AMissingBody_IsKeptOnReload_AndClearedOnlyByTheWatchersDelete()
    {
        // Arrange.
        var store = new PipelineDocumentStore();
        IDiagramDocumentReloader reloader = new PipelineDocumentReloader(store);
        Assert.NotEmpty(store.GetOrLoad(_root, Body).Model.Jobs);
        File.Delete(Body);

        // Act: a reload that finds no file.
        reloader.Reload(_root, Body);

        // Assert: kept until the absence is confirmed (R2.5's first half). A body missing on a
        // reload is far more often a publish in flight than a deletion, so the last good document
        // stays. This is NOT R2.4, which is a body present but unreadable - see the next test.
        Assert.NotEmpty(store.GetOrLoad(_root, Body).Model.Jobs);

        // Act: the watcher's evidence that it is gone.
        reloader.BodyDeleted(_root, Body);

        // Assert: cleared, to what a first open of a missing body shows (R2.5).
        Assert.Empty(store.GetOrLoad(_root, Body).Model.Jobs);
    }

    [Fact]
    public void AnUnreadableBody_IsKeptOnReload_AndTellsNobody()
    {
        // Arrange.
        var store = new PipelineDocumentStore();
        IDiagramDocumentReloader reloader = new PipelineDocumentReloader(store);
        Assert.NotEmpty(store.GetOrLoad(_root, Body).Model.Jobs);
        var told = 0;
        store.Changed += (_, _) => told++;

        // Act: a reload while another program holds the file with no sharing at all.
        using (new FileStream(Body, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            reloader.Reload(_root, Body);
        }

        // Assert: the last good pipeline is kept, not replaced by an empty one (R2.4), and since
        // nothing a session shows has changed, no session is told.
        Assert.NotEmpty(store.GetOrLoad(_root, Body).Model.Jobs);
        Assert.Equal(0, told);
    }

    [Fact]
    public void ADeletion_TellsTheSessions()
    {
        // Arrange.
        var store = new PipelineDocumentStore();
        IDiagramDocumentReloader reloader = new PipelineDocumentReloader(store);
        store.GetOrLoad(_root, Body);
        var told = new List<string>();
        store.Changed += (_, args) => told.Add(args.Path);
        File.Delete(Body);

        // Act.
        reloader.BodyDeleted(_root, Body);

        // Assert.
        Assert.Equal([Body], told);
    }
}
