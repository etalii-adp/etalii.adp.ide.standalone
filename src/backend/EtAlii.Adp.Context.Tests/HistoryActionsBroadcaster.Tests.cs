using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EtAlii.Adp.Context.Tests;

/// <summary>
/// The broadcaster turns a history change into one project-actions push. Driven with a fake
/// store it can raise <c>Changed</c> on by hand, a resolver it can count and make throw, a
/// selection store that records the pushes (diagram-undo-redo Requirement 5.3), and a fake clock
/// whose advance runs the coalescing window's timer in the test's own thread (code-structure
/// FR-051), so nothing here waits on a scheduler.
/// </summary>
public class HistoryActionsBroadcasterTests
{
    private const string Root = @"C:\project";
    private static readonly TimeSpan Window = HistoryActionsBroadcaster.CoalesceWindow;
    private static readonly TimeSpan JustShort = Window - TimeSpan.FromMilliseconds(1);

    private readonly FakeTimeProvider _clock = new();
    private readonly HistoryActionsBroadcasterFakeStore _store = new();
    private readonly HistoryActionsBroadcasterCountingResolver _resolver = new();
    private readonly HistoryActionsBroadcasterRecordingSelectionStore _selection = new();

    [Fact]
    public void OneChange_ResultsInOneDiscoveryAndOnePush_OnceTheWindowPasses()
    {
        // Arrange.
        using var broadcaster = new HistoryActionsBroadcaster(_store, _resolver, _selection, _clock);
        _store.Raise(Root);

        // Act and assert, step by step.
        _clock.Advance(JustShort);
        Assert.Empty(_selection.Pushes);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Single(_selection.Pushes);
        Assert.Equal(ProjectRoot.From(Root), _selection.Pushes[0].Root);
        Assert.Equal(1, _resolver.DiscoverCount);
    }

    [Fact]
    public void ABurstForOneProject_CollapsesIntoOnePush()
    {
        // Arrange.
        using var broadcaster = new HistoryActionsBroadcaster(_store, _resolver, _selection, _clock);

        // Act: several changes, each inside the window the one before it started - a redo that
        // touched several entries. Every change starts the window over.
        _store.Raise(Root);
        _clock.Advance(JustShort);
        _store.Raise(Root);
        _clock.Advance(JustShort);
        _store.Raise(Root);
        _clock.Advance(JustShort);

        // Assert, step by step.
        Assert.Empty(_selection.Pushes);
        _clock.Advance(Window);
        Assert.Single(_selection.Pushes);
        Assert.Equal(1, _resolver.DiscoverCount);
    }

    [Fact]
    public void ChangesInTwoProjects_EachGetTheirOwnPush()
    {
        // Arrange.
        const string other = @"C:\other";
        using var broadcaster = new HistoryActionsBroadcaster(_store, _resolver, _selection, _clock);

        // Act.
        _store.Raise(Root);
        _store.Raise(other);
        _clock.Advance(Window);

        // Assert.
        Assert.Equal(2, _selection.Pushes.Count);
        Assert.Contains(_selection.Pushes, push => push.Root == ProjectRoot.From(Root));
        Assert.Contains(_selection.Pushes, push => push.Root == ProjectRoot.From(other));
    }

    [Fact]
    public void AThrowingResolver_PushesNothingAndDoesNotEscape()
    {
        // Arrange.
        var resolver = new HistoryActionsBroadcasterCountingResolver { Throw = true };
        using var broadcaster = new HistoryActionsBroadcaster(_store, resolver, _selection, _clock);
        _store.Raise(Root);

        // Act: the discovery runs, and throws, inside this call.
        _clock.Advance(Window);

        // Assert.
        Assert.Empty(_selection.Pushes);
        Assert.Equal(1, resolver.DiscoverCount);
    }

    [Fact]
    public void AChangeAfterDispose_PushesNothing()
    {
        // Arrange.
        var broadcaster = new HistoryActionsBroadcaster(_store, _resolver, _selection, _clock);
        _store.Raise(Root);

        // Act.
        broadcaster.Dispose();
        _store.Raise(Root);
        _clock.Advance(Window);

        // Assert: neither the change pending at disposal nor the one after it.
        Assert.Empty(_selection.Pushes);
        Assert.Equal(0, _resolver.DiscoverCount);
    }
}
