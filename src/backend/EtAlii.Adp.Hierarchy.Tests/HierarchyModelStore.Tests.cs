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
        using var store = new HierarchyModelStore(TestToolRouters.For());
        var watchId = ShortGuid.NewShortGuid();

        // Act.
        var first = store.GetOrCreate(watchId, ProjectRoot.From(_root));
        var second = store.GetOrCreate(watchId, ProjectRoot.From(_root));

        // Assert.
        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrCreate_WithDifferentWatchIds_ForTheSameRoot_ReturnsIndependentModels()
    {
        // Arrange.
        using var store = new HierarchyModelStore(TestToolRouters.For());

        // Act.
        var first = store.GetOrCreate(ShortGuid.NewShortGuid(), ProjectRoot.From(_root));
        var second = store.GetOrCreate(ShortGuid.NewShortGuid(), ProjectRoot.From(_root));

        // Assert.
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Claim_ReturnsTheModelListEntriesMade_AndReleasingItDropsTheModel()
    {
        // Arrange.
        using var store = new HierarchyModelStore(TestToolRouters.For());
        var watchId = ShortGuid.NewShortGuid();
        var listed = store.GetOrCreate(watchId, ProjectRoot.From(_root));

        // Act.
        var claim = store.Claim(watchId, ProjectRoot.From(_root));
        claim.Dispose();

        // Assert.
        Assert.Same(listed, claim.Model);
        Assert.NotSame(listed, store.GetOrCreate(watchId, ProjectRoot.From(_root)));
    }

    [Fact]
    public void IdleEntry_WithNoWatcherAttached_IsEvictedAfterTheTimeout()
    {
        // Arrange.
        using var store = new HierarchyModelStore(TestToolRouters.For(), IdleTimeout, _clock);
        var watchId = ShortGuid.NewShortGuid();
        var first = store.GetOrCreate(watchId, ProjectRoot.From(_root));

        // Act and assert, step by step: kept until the timeout, dropped once it passes.
        _clock.Advance(IdleTimeout - TimeSpan.FromMilliseconds(1));
        Assert.Same(first, store.GetOrCreate(watchId, ProjectRoot.From(_root)));

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.NotSame(first, store.GetOrCreate(watchId, ProjectRoot.From(_root)));
    }

    [Fact]
    public void Claiming_CancelsTheIdleEviction()
    {
        // Arrange.
        using var store = new HierarchyModelStore(TestToolRouters.For(), IdleTimeout, _clock);
        var watchId = ShortGuid.NewShortGuid();
        var first = store.GetOrCreate(watchId, ProjectRoot.From(_root));
        using var claim = store.Claim(watchId, ProjectRoot.From(_root));

        _clock.Advance(IdleTimeout * 2);

        // Act and assert, step by step.
        var second = store.GetOrCreate(watchId, ProjectRoot.From(_root));
        Assert.Same(first, second);
    }
}
