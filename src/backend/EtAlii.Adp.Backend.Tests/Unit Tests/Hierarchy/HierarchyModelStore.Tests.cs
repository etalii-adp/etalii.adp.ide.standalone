using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class HierarchyModelStoreTests : IDisposable
{
    private readonly string _root;

    public HierarchyModelStoreTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void GetOrCreate_CalledTwiceWithTheSameWatchId_ReturnsTheSameModelInstance()
    {
        using var store = new HierarchyModelStore();
        var watchId = ShortGuid.NewShortGuid();

        var first = store.GetOrCreate(watchId, _root);
        var second = store.GetOrCreate(watchId, _root);

        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrCreate_WithDifferentWatchIds_ForTheSameRoot_ReturnsIndependentModels()
    {
        using var store = new HierarchyModelStore();

        var first = store.GetOrCreate(ShortGuid.NewShortGuid(), _root);
        var second = store.GetOrCreate(ShortGuid.NewShortGuid(), _root);

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task Remove_StopsTheAttachedWatcherFromRaisingFurtherEvents()
    {
        using var store = new HierarchyModelStore();
        var watchId = ShortGuid.NewShortGuid();
        store.GetOrCreate(watchId, _root);
        var eventCount = 0;
        var watcher = new RootFolderWatcher(_root, (_, _, _) => Interlocked.Increment(ref eventCount), _ => { });
        store.AttachWatcher(watchId, watcher);

        store.Remove(watchId);
        File.WriteAllText(IoPath.Combine(_root, "after-remove.txt"), "");
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        Assert.Equal(0, eventCount);
    }

    [Fact]
    public async Task IdleEntry_WithNoWatcherAttached_IsEvictedAfterTheTimeout()
    {
        using var store = new HierarchyModelStore(idleTimeout: TimeSpan.FromMilliseconds(50));
        var watchId = ShortGuid.NewShortGuid();
        var first = store.GetOrCreate(watchId, _root);

        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        var second = store.GetOrCreate(watchId, _root);
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task AttachingAWatcher_CancelsTheIdleEviction()
    {
        using var store = new HierarchyModelStore(idleTimeout: TimeSpan.FromMilliseconds(50));
        var watchId = ShortGuid.NewShortGuid();
        var first = store.GetOrCreate(watchId, _root);
        using var watcher = new RootFolderWatcher(_root, (_, _, _) => { }, _ => { });
        store.AttachWatcher(watchId, watcher);

        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        var second = store.GetOrCreate(watchId, _root);
        Assert.Same(first, second);
    }
}
