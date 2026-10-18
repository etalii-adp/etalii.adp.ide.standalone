using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.TestSupport;

/// <summary>
/// What every diagram module's document store must do, written once (code-structure FR-035): an
/// edit is not lost to a reload, the store's own save does not come back as somebody else's, an
/// external write is read, a read that fails keeps the last good document, and a deleted body ends
/// the document. A module's test class derives from this and supplies its store, an example and an
/// edit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the lost edit is the first fact.</b> A command loads, edits the document in memory, and
/// saves. Until 2026-09-24 every writable store saved whatever its cache held, so a reload landing
/// between the edit and the save wrote the re-read file AND REPORTED SUCCESS, and the command's
/// inverse went onto the undo stack for a change the file never received. Tests that save untouched
/// documents cannot see it, because the re-read document and the discarded one are byte-identical;
/// the precondition is an edit that exists in memory and nowhere else.
/// </para>
/// <para>
/// <b>Deterministic on purpose.</b> Calling <c>Reload</c> where the watcher's notification would land
/// is the same ordering as the race, without the wait.
/// </para>
/// </remarks>
public abstract class DocumentStoreContract<TStore, TBody, TModel> : IDisposable
    where TStore : IDiagramDocumentStore<TBody, TModel>
{
    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.DocumentStoreContract");

    /// <summary>A module example with at least one element, copied to a scratch folder for each test.</summary>
    protected abstract string Example { get; }

    /// <summary>The copy of <see cref="Example"/> the test works on, made on first use.</summary>
    private string Body => field ??= CopyExample();

    /// <summary>A text that <see cref="Edit"/> puts into a body, and that is in no example.</summary>
    protected abstract string EditMarker { get; }

    /// <summary>A new store, with nothing loaded.</summary>
    protected abstract TStore CreateStore();

    /// <summary>Changes <paramref name="document"/> in memory so that its text holds <see cref="EditMarker"/>.</summary>
    protected abstract void Edit(TBody document);

    /// <summary>The text <paramref name="document"/> is saved as.</summary>
    protected abstract string TextOf(TBody document);

    /// <summary>Whether <paramref name="model"/> holds no element.</summary>
    protected abstract bool IsEmpty(TModel model);

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
    public async Task ASaveAfterAReload_WritesTheEditRatherThanTheFileItJustReread()
    {
        // Arrange: loaded, then edited in memory only.
        var store = CreateStore();
        var document = store.GetOrLoad(Body).Document;
        Edit(document);
        Assert.Contains(EditMarker, TextOf(document), StringComparison.Ordinal);

        // Act: the notification for an earlier write of this file lands between the edit and the save.
        store.Reload(Body);
        var saved = await store.SaveAsync(Body, document, TestContext.Current.CancellationToken);

        // Assert: the save reported success, so the edit it reported success for is in the file.
        Assert.False(saved.Failed, saved.Error);
        var written = await File.ReadAllTextAsync(Body, TestContext.Current.CancellationToken);
        Assert.Contains(EditMarker, written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheStoresOwnSave_DoesNotComeBackAsAReload()
    {
        // Arrange: an edit saved, and the sessions told of it once.
        var store = CreateStore();
        var document = store.GetOrLoad(Body).Document;
        Edit(document);
        var told = new List<string>();
        store.Changed += (_, args) => told.Add(args.Path);
        Assert.False((await store.SaveAsync(Body, document, TestContext.Current.CancellationToken)).Failed);
        var saved = store.GetOrLoad(Body);

        // Act: the watcher's notification for that same save.
        store.Reload(Body);

        // Assert: not an external change, so nothing is re-read and nobody is told twice.
        Assert.Equal([Body], told);
        Assert.Same(saved, store.GetOrLoad(Body));
    }

    [Fact]
    public async Task AnExternalWrite_IsRead_AndTellsTheSessions()
    {
        // Arrange: this store has the document open; another writer edits the file.
        var store = CreateStore();
        Assert.DoesNotContain(EditMarker, TextOf(store.GetOrLoad(Body).Document), StringComparison.Ordinal);
        var other = CreateStore();
        var document = other.GetOrLoad(Body).Document;
        Edit(document);
        Assert.False((await other.SaveAsync(Body, document, TestContext.Current.CancellationToken)).Failed);
        var told = new List<string>();
        store.Changed += (_, args) => told.Add(args.Path);

        // Act.
        store.Reload(Body);

        // Assert.
        Assert.Contains(EditMarker, TextOf(store.GetOrLoad(Body).Document), StringComparison.Ordinal);
        Assert.Equal([Body], told);
    }

    [Fact]
    public void AReadThatFails_KeepsTheLastGoodDocument()
    {
        // Arrange: open, then the body goes missing - as it does for an instant during a publish.
        var store = CreateStore();
        var loaded = store.GetOrLoad(Body);
        Assert.False(IsEmpty(loaded.Model));
        var told = new List<string>();
        store.Changed += (_, args) => told.Add(args.Path);
        File.Delete(Body);

        // Act.
        store.Reload(Body);

        // Assert: the last good document stays, and the sessions have nothing to redraw.
        Assert.Same(loaded, store.GetOrLoad(Body));
        Assert.Empty(told);
    }

    [Fact]
    public void ADeletedBody_EndsAsANewDocument_AndTellsTheSessions()
    {
        // Arrange.
        var store = CreateStore();
        Assert.False(IsEmpty(store.GetOrLoad(Body).Model));
        var told = new List<string>();
        store.Changed += (_, args) => told.Add(args.Path);
        File.Delete(Body);

        // Act.
        store.BodyDeleted(Body);

        // Assert: what a first open of a missing body shows, and usable, so a new element can be saved into it.
        var entry = store.GetOrLoad(Body);
        Assert.True(IsEmpty(entry.Model));
        Assert.True(entry.IsUsable);
        Assert.Equal([Body], told);
    }
}
