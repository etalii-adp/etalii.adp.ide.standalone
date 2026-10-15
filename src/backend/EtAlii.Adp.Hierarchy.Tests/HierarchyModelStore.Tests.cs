using Microsoft.Extensions.Time.Testing;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

public class HierarchyModelStoreTests : IDisposable
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    private readonly FakeTimeProvider _clock = new();
    private readonly string _root;

    public HierarchyModelStoreTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public void GetOrCreate_CalledTwiceWithTheSameWatchId_ReturnsTheSameModelInstance()
    {
        // Arrange.
        using var store = new HierarchyModelStore();
        var watchId = ShortGuid.NewShortGuid();

        // Act.
        var first = store.GetOrCreate(watchId, _root);
        var second = store.GetOrCreate(watchId, _root);

        // Assert.
        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrCreate_WithDifferentWatchIds_ForTheSameRoot_ReturnsIndependentModels()
    {
        // Arrange.
        using var store = new HierarchyModelStore();

        // Act.
        var first = store.GetOrCreate(ShortGuid.NewShortGuid(), _root);
        var second = store.GetOrCreate(ShortGuid.NewShortGuid(), _root);

        // Assert.
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task Remove_StopsTheAttachedWatcherFromRaisingFurtherEvents()
    {
        // Arrange.
        using var store = new HierarchyModelStore();
        var watchId = ShortGuid.NewShortGuid();
        store.GetOrCreate(watchId, _root);
        var eventCount = 0;
        var watcher = new RootFolderWatcher(_root, (_, _, _) => Interlocked.Increment(ref eventCount), _ => { });
        store.AttachWatcher(watchId, watcher);

        // Act.
        store.Remove(watchId);
        await File.WriteAllTextAsync(IoPath.Combine(_root, "after-remove.txt"), "", TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void IdleEntry_WithNoWatcherAttached_IsEvictedAfterTheTimeout()
    {
        // Arrange.
        using var store = new HierarchyModelStore(idleTimeout: IdleTimeout, time: _clock);
        var watchId = ShortGuid.NewShortGuid();
        var first = store.GetOrCreate(watchId, _root);

        // Act and assert, step by step: kept until the timeout, dropped once it passes.
        _clock.Advance(IdleTimeout - TimeSpan.FromMilliseconds(1));
        Assert.Same(first, store.GetOrCreate(watchId, _root));

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.NotSame(first, store.GetOrCreate(watchId, _root));
    }

    [Fact]
    public void AttachingAWatcher_CancelsTheIdleEviction()
    {
        // Arrange.
        using var store = new HierarchyModelStore(idleTimeout: IdleTimeout, time: _clock);
        var watchId = ShortGuid.NewShortGuid();
        var first = store.GetOrCreate(watchId, _root);
        using var watcher = new RootFolderWatcher(_root, (_, _, _) => { }, _ => { });
        store.AttachWatcher(watchId, watcher);

        _clock.Advance(IdleTimeout * 2);

        // Act and assert, step by step.
        var second = store.GetOrCreate(watchId, _root);
        Assert.Same(first, second);
    }
}
