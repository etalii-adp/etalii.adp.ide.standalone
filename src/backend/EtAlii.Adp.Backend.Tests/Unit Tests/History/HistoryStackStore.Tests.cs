using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// One history per project, keyed by resolved root path, with the same idle-grace eviction the
/// selection store uses so a reconnect within the window keeps its undo history
/// (diagram-undo-redo Requirements 1.1, 4.1, 4.2).
/// </summary>
public class HistoryStackStoreTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static string TempPath() =>
        IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Get_ForOnePath_ReturnsTheSameInstanceEveryTime()
    {
        using var store = new HistoryStackStore(new HistoryStackStoreStubDispatcher());
        var path = TempPath();

        Assert.Same(store.Get(path), store.Get(path));
    }

    [Fact]
    public void Get_ForTwoPaths_ReturnsDifferentInstances()
    {
        using var store = new HistoryStackStore(new HistoryStackStoreStubDispatcher());

        Assert.NotSame(store.Get(TempPath()), store.Get(TempPath()));
    }

    [Fact]
    public void Get_IsCaseInsensitiveOnTheKey()
    {
        // Two connections that opened the same folder differently-cased share one history.
        using var store = new HistoryStackStore(new HistoryStackStoreStubDispatcher());
        var path = TempPath();

        Assert.Same(store.Get(path.ToUpperInvariant()), store.Get(path.ToLowerInvariant()));
    }

    [Fact]
    public async Task Release_PastTheLastRetain_DropsTheStackAfterTheGrace()
    {
        using var store = new HistoryStackStore(new HistoryStackStoreStubDispatcher(), TimeSpan.FromMilliseconds(100));
        var path = TempPath();
        store.Retain(path);
        var first = store.Get(path);

        store.Release(path);

        // Past the grace, the stack is dropped and the next ask builds a fresh one.
        await WaitUntilAsync(() => !ReferenceEquals(store.Get(path), first));
    }

    [Fact]
    public async Task Retain_WithinTheGraceWindow_KeepsTheStack()
    {
        using var store = new HistoryStackStore(new HistoryStackStoreStubDispatcher(), TimeSpan.FromMilliseconds(100));
        var path = TempPath();
        store.Retain(path);
        var first = store.Get(path);

        store.Release(path); // starts the grace
        store.Retain(path);  // reconnect within it cancels the eviction

        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Same(first, store.Get(path));
        store.Release(path);
    }

    [Fact]
    public async Task Changed_CarriesTheRootPathOfTheStackThatChanged()
    {
        using var store = new HistoryStackStore(new HistoryStackStoreStubDispatcher());
        var path = TempPath();
        HistoryChangedEventArgs? captured = null;
        store.Changed += (_, args) => captured = args;

        // A recorded command on this project's stack aggregates up to the store's own event.
        await store.Get(path).ExecuteAsync(new HistoryStackStoreNoop(), TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => captured is not null);
        Assert.Equal(IoPath.GetFullPath(path), captured!.RootPath);
    }

    [Fact]
    public async Task Release_PastTheLastRetain_DisposesTheDroppedStack()
    {
        using var store = new HistoryStackStore(new HistoryStackStoreStubDispatcher(), TimeSpan.FromMilliseconds(100));
        var path = TempPath();
        store.Retain(path);
        var dropped = store.Get(path);
        store.Release(path);
        await WaitUntilAsync(() => !ReferenceEquals(store.Get(path), dropped));

        // The dropped stack was disposed - which is what stops it leaking - so using it now
        // is an error, and that error is how the test knows it was disposed.
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await dropped.ExecuteAsync(new HistoryStackStoreNoop(), TestContext.Current.CancellationToken));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition(), "The awaited condition did not hold within the timeout.");
    }

}
