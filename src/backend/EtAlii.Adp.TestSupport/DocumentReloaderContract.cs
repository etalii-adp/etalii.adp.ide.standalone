using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.TestSupport;

/// <summary>
/// What every diagram module's reload seam must do, written once (code-structure FR-035): a deleted
/// body is cleared, a body that merely could not be read is kept, the sessions hear of a deletion,
/// and a forgotten document is read afresh. A module's test class derives from this and supplies
/// its store, an example and how to tell an empty document.
/// </summary>
/// <remarks>
/// <para>
/// <b>Seen to fail with <see cref="DiagramDocumentReloader{TStore}.BodyDeletedAsync"/> routed to
/// <c>ReloadAsync</c></b>, which is what the interface's default does: the reload finds no file, keeps the
/// last good document, and the deleted diagram stays open with its old elements.
/// </para>
/// <para>
/// <b>The reloader is held as <see cref="IDiagramDocumentReloader"/>, and that is load-bearing.</b>
/// A default interface member is callable only through the interface. Held as the class, removing
/// the override would stop this file compiling instead of making the test fail, so the sabotage
/// could never be seen red. Do not "simplify" the declared type.
/// </para>
/// </remarks>
public abstract class DocumentReloaderContract<TStore> : IDisposable
    where TStore : IDiagramDocumentStore
{
    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.DocumentReloaderContract");

    /// <summary>The module's diagram type.</summary>
    protected abstract ToolOrigin Origin { get; }

    /// <summary>A module example with at least one element, copied to a scratch folder for each test.</summary>
    protected abstract string Example { get; }

    /// <summary>The copy of <see cref="Example"/> the test works on, made on first use.</summary>
    private string Body => field ??= CopyExample();

    /// <summary>A new store, with nothing loaded.</summary>
    protected abstract TStore CreateStore();

    /// <summary>Opens the document at <paramref name="path"/> and says whether it holds no element.</summary>
    protected abstract Task<bool> IsEmptyAsync(TStore store, string path, CancellationToken cancellationToken);

    /// <summary>Calls <paramref name="changed"/> with the path each time the store tells its sessions of a change.</summary>
    protected abstract void OnChanged(TStore store, Action<string> changed);

    private string CopyExample()
    {
        var body = _folder.Combine(Path.GetFileName(Example));
        File.Copy(Example, body);
        return body;
    }

    public void Dispose()
    {
        _folder.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AMissingBody_IsKeptOnReload_AndClearedOnlyByTheWatchersDelete()
    {
        // Arrange.
        var store = CreateStore();
        IDiagramDocumentReloader reloader = new DiagramDocumentReloader<TStore>(Origin, store);
        Assert.False(await IsEmptyAsync(store, Body, TestContext.Current.CancellationToken));
        File.Delete(Body);

        // Act: a reload that finds no file.
        await reloader.ReloadAsync(_folder.Path, Body, TestContext.Current.CancellationToken);

        // Assert: kept. A body missing on a reload is far more often a publish in flight than a
        // deletion, so the last good document stays.
        Assert.False(await IsEmptyAsync(store, Body, TestContext.Current.CancellationToken));

        // Act: the watcher's evidence that it is gone.
        await reloader.BodyDeletedAsync(_folder.Path, Body, TestContext.Current.CancellationToken);

        // Assert: cleared, to what a first open of a missing body would show.
        Assert.True(await IsEmptyAsync(store, Body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADeletion_TellsTheSessions()
    {
        // Arrange.
        var store = CreateStore();
        IDiagramDocumentReloader reloader = new DiagramDocumentReloader<TStore>(Origin, store);
        Assert.False(await IsEmptyAsync(store, Body, TestContext.Current.CancellationToken));
        var told = new List<string>();
        OnChanged(store, told.Add);
        File.Delete(Body);

        // Act.
        await reloader.BodyDeletedAsync(_folder.Path, Body, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal([Body], told);
    }

    [Fact]
    public async Task AForgottenDocument_IsReadAfreshOnTheNextOpen()
    {
        // Arrange.
        var store = CreateStore();
        Assert.False(await IsEmptyAsync(store, Body, TestContext.Current.CancellationToken));
        File.Delete(Body);

        // Act.
        store.Forget(Body);

        // Assert: the next open reads the file, which is gone, so the document is a new one.
        Assert.True(await IsEmptyAsync(store, Body, TestContext.Current.CancellationToken));
    }
}
