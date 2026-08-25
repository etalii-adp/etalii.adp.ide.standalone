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
        // Arrange and act.
        using var store = new HistoryStackStore(new HistoryStackStoreStubDispatcher());
        var path = TempPath();

        // Assert.
        Assert.Same(store.Get(path), store.Get(path));
    }

    [Fact]
    public void Get_ForTwoPaths_ReturnsDifferentInstances()
    {
        // Act.
        using var store = new HistoryStackStore(new HistoryStackStoreStubDispatcher());

        // Assert.
        Assert.NotSame(store.Get(TempPath()), store.Get(TempPath()));
    }

    [Fact]
    public void Get_IsCaseInsensitiveOnTheKey()
    {
        // Arrange and act.
        // Two connections that opened the same folder differently-cased share one history.
        using var store = new HistoryStackStore(new HistoryStackStoreStubDispatcher());
        var path = TempPath();

        // Assert.
        Assert.Same(store.Get(path.ToUpperInvariant()), store.Get(path.ToLowerInvariant()));
    }

    [Fact]
    public async Task Release_PastTheLastRetain_DropsTheStackAfterTheGrace()
    {
        // Arrange.
        using var store = new HistoryStackStore(new HistoryStackStoreStubDispatcher(), TimeSpan.FromMilliseconds(100));
        var path = TempPath();
        store.Retain(path);
        var first = store.Get(path);

        // Act.
        store.Release(path);

        // Assert: past the grace, the stack is dropped and the next ask builds a fresh one.
        await WaitUntilAsync(() => !ReferenceEquals(store.Get(path), first));
    }

    [Fact]
    public async Task Retain_WithinTheGraceWindow_KeepsTheStack()
    {
        // Arrange.
        using var store = new HistoryStackStore(new HistoryStackStoreStubDispatcher(), TimeSpan.FromMilliseconds(100));
        var path = TempPath();
        store.Retain(path);
        var first = store.Get(path);

        store.Release(path); // starts the grace
        store.Retain(path);  // reconnect within it cancels the eviction

        // Act and assert, step by step.
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Same(first, store.Get(path));
        store.Release(path);
    }

    [Fact]
    public async Task Changed_CarriesTheRootPathOfTheStackThatChanged()
    {
        // Arrange.
        using var store = new HistoryStackStore(new HistoryStackStoreStubDispatcher());
        var path = TempPath();
        HistoryChangedEventArgs? captured = null;
        store.Changed += (_, args) => captured = args;

        // A recorded command on this project's stack aggregates up to the store's own event.
        await store.Get(path).ExecuteAsync(new HistoryStackStoreNoop(), TestContext.Current.CancellationToken);

        // Act and assert, step by step.
        await WaitUntilAsync(() => captured is not null);
        Assert.Equal(IoPath.GetFullPath(path), captured!.RootPath);
    }

    [Fact]
    public async Task Release_PastTheLastRetain_DisposesTheDroppedStack()
    {
        // Arrange and act.
        using var store = new HistoryStackStore(new HistoryStackStoreStubDispatcher(), TimeSpan.FromMilliseconds(100));
        var path = TempPath();
        store.Retain(path);
        var dropped = store.Get(path);
        store.Release(path);
        await WaitUntilAsync(() => !ReferenceEquals(store.Get(path), dropped));

        // Assert.
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
