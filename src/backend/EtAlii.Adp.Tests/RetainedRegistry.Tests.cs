using System.Runtime.CompilerServices;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EtAlii.Adp.Tests;

/// <summary>
/// The registry every idle-eviction store of the backend holds its values in (code-structure
/// FR-051). A fake clock runs its windows inside <see cref="FakeTimeProvider.Advance"/>, so each
/// test says exactly when a window passes.
/// </summary>
public sealed class RetainedRegistryTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan JustShort = Window - TimeSpan.FromMilliseconds(1);

    private readonly FakeTimeProvider _clock = new();
    private readonly List<string> _dropped = [];

    private RetainedRegistry<string, object> Registry(bool dropUnclaimed = false) =>
        new(Window, (key, _) => _dropped.Add(key), _clock, dropUnclaimed);

    private static object Create(string key) => new();

    [Fact]
    public void GetOrAdd_MakesAValueOnce()
    {
        // Arrange.
        using var registry = Registry();
        var made = 0;

        // Act.
        var first = registry.GetOrAdd("a", _ => { made++; return new object(); });
        var second = registry.GetOrAdd("a", _ => { made++; return new object(); });

        // Assert.
        Assert.Same(first, second);
        Assert.Equal(1, made);
    }

    [Fact]
    public void TheLastRelease_StartsTheWindow_AndTheValueGoesWhenItPasses()
    {
        // Arrange.
        using var registry = Registry();
        var value = registry.Retain("a", Create);
        registry.Retain("a", Create);

        // Act and assert, step by step: one release of two holds it still.
        registry.Release("a");
        _clock.Advance(Window * 2);
        Assert.Empty(_dropped);

        registry.Release("a");
        _clock.Advance(JustShort);
        Assert.Empty(_dropped);
        Assert.True(registry.TryGet("a", out var kept));
        Assert.Same(value, kept);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(["a"], _dropped);
        Assert.False(registry.TryGet("a", out _));
    }

    [Fact]
    public void ARetainInsideTheWindow_KeepsTheValue()
    {
        // Arrange.
        using var registry = Registry();
        var value = registry.Retain("a", Create);
        registry.Release("a");
        _clock.Advance(JustShort);

        // Act.
        var again = registry.Retain("a", Create);
        _clock.Advance(Window * 2);

        // Assert.
        Assert.Same(value, again);
        Assert.Empty(_dropped);
    }

    [Fact]
    public void AnUnclaimedValue_IsKept_UnlessTheOwnerAskedOtherwise()
    {
        // Arrange.
        using var keeping = Registry();
        using var dropping = Registry(dropUnclaimed: true);

        // Act.
        keeping.GetOrAdd("kept", Create);
        dropping.GetOrAdd("dropped", Create);
        _clock.Advance(Window);

        // Assert.
        Assert.Equal(["dropped"], _dropped);
        Assert.True(keeping.TryGet("kept", out _));
    }

    [Fact]
    public void ClaimingAnUnclaimedValue_StopsItsWindow()
    {
        // Arrange.
        using var registry = Registry(dropUnclaimed: true);
        registry.GetOrAdd("a", Create);
        _clock.Advance(JustShort);

        // Act.
        var claimed = registry.TryRetain("a");
        _clock.Advance(Window * 2);

        // Assert.
        Assert.True(claimed);
        Assert.Empty(_dropped);
    }

    [Fact]
    public void TryRetain_MakesNothing()
    {
        // Arrange.
        using var registry = Registry();

        // Act.
        var retained = registry.TryRetain("a");

        // Assert.
        Assert.False(retained);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Remove_DropsAtOnce_RetainedOrNot_AndItsWindowNoLongerRuns()
    {
        // Arrange.
        using var registry = Registry(dropUnclaimed: true);
        registry.Retain("held", Create);
        registry.GetOrAdd("unclaimed", Create);

        // Act.
        var removedHeld = registry.Remove("held");
        var removedUnclaimed = registry.Remove("unclaimed");
        var removedNothing = registry.Remove("never");
        _clock.Advance(Window * 2);

        // Assert: each dropped once, by the removal.
        Assert.True(removedHeld);
        Assert.True(removedUnclaimed);
        Assert.False(removedNothing);
        Assert.Equal(["held", "unclaimed"], _dropped);
    }

    [Fact]
    public void ANewValueAfterADrop_HasAWindowOfItsOwn()
    {
        // Arrange: the first value goes, and a second is made under the same key.
        using var registry = Registry(dropUnclaimed: true);
        var first = registry.GetOrAdd("a", Create);
        _clock.Advance(Window);
        var second = registry.GetOrAdd("a", Create);

        // Act and assert, step by step.
        Assert.NotSame(first, second);
        _clock.Advance(JustShort);
        Assert.Equal(["a"], _dropped);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(["a", "a"], _dropped);
    }

    [Fact]
    public void Dispose_DropsEveryValue_AndStartsNoWindowMore()
    {
        // Arrange.
        var registry = Registry(dropUnclaimed: true);
        registry.Retain("held", Create);
        registry.GetOrAdd("unclaimed", Create);

        // Act.
        registry.Dispose();
        _clock.Advance(Window * 2);

        // Assert: each dropped once, by the disposal.
        Assert.Equal(["held", "unclaimed"], _dropped.Order());
    }

    [Fact]
    public void TheComparer_DecidesWhichKeysAreOne()
    {
        // Arrange.
        using var registry = new RetainedRegistry<string, object>(
            Window, (key, _) => _dropped.Add(key), _clock, comparer: StringComparer.OrdinalIgnoreCase);

        // Act.
        var first = registry.Retain("/Project", Create);
        var second = registry.Retain("/project", Create);

        // Assert.
        Assert.Same(first, second);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void TheDropCallback_MayUseTheRegistry()
    {
        // Arrange: an owner whose drop reaches back in, as a store that logs its count does.
        var counts = new List<int>();
        var self = new StrongBox<RetainedRegistry<string, object>>();
        using var registry = self.Value = new RetainedRegistry<string, object>(Window, (_, _) => counts.Add(self.Value!.Count), _clock);
        registry.Retain("a", Create);
        registry.Retain("b", Create);

        // Act: one by the window, one by removal.
        registry.Release("a");
        _clock.Advance(Window);
        registry.Remove("b");

        // Assert.
        Assert.Equal([1, 0], counts);
    }

    // ---- a window of zero: claim-counted, no grace (code-structure T294) --------------------------

    [Fact]
    public void WithNoWindow_TheLastReleaseDropsAtOnce_AndOneOfTwoKeepsTheValue()
    {
        // Arrange.
        using var registry = new RetainedRegistry<string, object>(TimeSpan.Zero, (key, _) => _dropped.Add(key), _clock);
        var value = registry.Retain("a", Create);
        registry.Retain("a", Create);

        // Act and assert, step by step, with the clock never moving: one release of two holds it still.
        registry.Release("a");
        Assert.Empty(_dropped);
        Assert.True(registry.TryGet("a", out var kept));
        Assert.Same(value, kept);

        registry.Release("a");
        Assert.Equal(["a"], _dropped);
        Assert.False(registry.TryGet("a", out _));
        Assert.NotSame(value, registry.Retain("a", Create));
    }

    [Fact]
    public void WithNoWindow_AnUnclaimedValueIsKept_UntilARelease()
    {
        // Arrange.
        using var registry = new RetainedRegistry<string, object>(TimeSpan.Zero, (key, _) => _dropped.Add(key), _clock);
        registry.GetOrAdd("a", Create);
        _clock.Advance(Window * 2);
        Assert.Empty(_dropped);

        // Act.
        registry.Release("a");

        // Assert.
        Assert.Equal(["a"], _dropped);
    }

    [Fact]
    public void WithNoWindow_DisposeDropsEveryValueOnce_AndAgainIsHarmless()
    {
        // Arrange.
        var registry = new RetainedRegistry<string, object>(TimeSpan.Zero, (key, _) => _dropped.Add(key), _clock);
        registry.Retain("held", Create);
        registry.GetOrAdd("unclaimed", Create);

        // Act.
        registry.Dispose();
        registry.Dispose();

        // Assert.
        Assert.Equal(["held", "unclaimed"], _dropped.Order());
    }

    [Fact]
    public void ANegativeWindow_OrNoWindowWithUnclaimedDrops_IsRefused()
    {
        // Act and assert: with no window there is none in which to drop an unclaimed value.
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetainedRegistry<string, object>(TimeSpan.FromSeconds(-1), (_, _) => { }, _clock));
        Assert.Throws<ArgumentException>(() => new RetainedRegistry<string, object>(TimeSpan.Zero, (_, _) => { }, _clock, dropUnclaimed: true));
        Assert.Throws<ArgumentNullException>(() => new RetainedRegistry<string, object>(TimeSpan.Zero, (_, _) => { }, null!));
    }
}
