using Microsoft.Extensions.Time.Testing;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.History.Tests;

/// <summary>
/// One history per project, keyed by resolved root path, with the same idle-grace eviction the
/// selection store uses so a reconnect within the window keeps its undo history
/// (diagram-undo-redo Requirements 1.1, 4.1, 4.2). The grace runs on a fake clock, so a test
/// says exactly when it passes (code-structure FR-051).
/// </summary>
public class HistoryStackStoreTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    private readonly FakeTimeProvider _clock = new();

    private HistoryStackStore GracedStore() => new(new HistoryStackStoreStubDispatcher(), Grace, time: _clock);

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
    public void Release_PastTheLastRetain_DropsTheStackAfterTheGrace()
    {
        // Arrange.
        using var store = GracedStore();
        var path = TempPath();
        store.Retain(path);
        var first = store.Get(path);

        // Act.
        store.Release(path);

        // Assert, step by step: kept through the grace, dropped once it passes, and the next ask
        // builds a fresh one.
        _clock.Advance(Grace - TimeSpan.FromMilliseconds(1));
        Assert.Same(first, store.Get(path));

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.NotSame(first, store.Get(path));
    }

    [Fact]
    public void Retain_WithinTheGraceWindow_KeepsTheStack()
    {
        // Arrange.
        using var store = GracedStore();
        var path = TempPath();
        store.Retain(path);
        var first = store.Get(path);

        store.Release(path); // starts the grace
        _clock.Advance(Grace / 2);
        store.Retain(path);  // reconnect within it cancels the eviction

        // Act and assert, step by step.
        _clock.Advance(Grace * 2);
        Assert.Same(first, store.Get(path));
        store.Release(path);
    }

    [Fact]
    public void AStackOnlyRead_IsKept_BecauseNothingClaimedIt()
    {
        // Arrange: a provider reaches the stack without any connection retaining it.
        using var store = GracedStore();
        var path = TempPath();
        var first = store.Get(path);

        // Act.
        _clock.Advance(Grace * 2);

        // Assert.
        Assert.Same(first, store.Get(path));
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
        using var store = GracedStore();
        var path = TempPath();
        store.Retain(path);
        var dropped = store.Get(path);
        store.Release(path);
        Assert.False(IsDisposed(dropped), "The stack was disposed before its grace passed.");

        // Assert.
        // The dropped stack was disposed - which is what stops it leaking - so using it now
        // is an error, and that error is how the test knows it was disposed. The eviction runs
        // inside Advance, removal and disposal both, so there is no window left to wait out.
        _clock.Advance(Grace);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await dropped.ExecuteAsync(new HistoryStackStoreNoop(), TestContext.Current.CancellationToken));
    }

    /// <summary>Whether the stack refuses work because it has been disposed.</summary>
    private static bool IsDisposed(IHistoryStack stack)
    {
        try
        {
            stack.ExecuteAsync(new HistoryStackStoreNoop(), CancellationToken.None).GetAwaiter().GetResult();
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
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
