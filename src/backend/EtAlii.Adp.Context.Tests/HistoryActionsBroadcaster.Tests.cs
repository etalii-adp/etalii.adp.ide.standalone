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
    public async Task ADiscoveryStillRunning_DoesNotHoldTheClocksThread()
    {
        // Arrange: a discovery that answers only when it is let go.
        var resolver = new GatedResolver();
        using var broadcaster = new HistoryActionsBroadcaster(_store, resolver, _selection, _clock);
        _store.Raise(Root);

        // Act.
        await AdvanceOffThreadAsync();

        // Assert: nothing pushed yet, and the push follows once the discovery answers.
        Assert.Empty(_selection.Pushes);
        resolver.Answer();
        Assert.Single(_selection.Pushes);
        await broadcaster.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsync_CancelsTheDiscoveryUnderWay_AndReturnsOnceItHas()
    {
        // Arrange: a discovery that, once cancelled, finishes only when it is let go.
        var resolver = new GatedResolver { HoldsOnCancel = true };
        using var broadcaster = new HistoryActionsBroadcaster(_store, resolver, _selection, _clock);
        _store.Raise(Root);
        await AdvanceOffThreadAsync();

        // Act.
        var stopping = broadcaster.StopAsync(TestContext.Current.CancellationToken);

        // Assert: still stopping while the discovery finishes, stopped once it has, nothing pushed.
        var first = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.False(first == stopping, "StopAsync returned while the discovery it cancelled was still running.");
        resolver.LetFinish();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(resolver.Cancelled);
        Assert.Empty(_selection.Pushes);
    }

    [Fact]
    public async Task DisposingTwice_AfterStopAsync_DoesNotThrow()
    {
        // The host stops its services and may dispose its provider more than once.
        var broadcaster = new HistoryActionsBroadcaster(_store, _resolver, _selection, _clock);
        await broadcaster.StopAsync(TestContext.Current.CancellationToken);

        broadcaster.Dispose();
        broadcaster.Dispose();
    }

    [Fact]
    public async Task StopAsync_AfterDispose_DoesNotThrow()
    {
        // The host can dispose a service before it stops it: a test host's fixture disposed the
        // broadcaster first, and its StopAsync then threw on the disposed token source.
        var broadcaster = new HistoryActionsBroadcaster(_store, _resolver, _selection, _clock);
        broadcaster.Dispose();

        await broadcaster.StopAsync(TestContext.Current.CancellationToken);
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

    /// <summary>
    /// Lets the window pass on another thread, so a timer callback that waits on the discovery
    /// fails the test as an advance that never returns, rather than holding the test's own thread.
    /// </summary>
    private async Task AdvanceOffThreadAsync()
    {
        var advancing = Task.Run(() => _clock.Advance(Window), TestContext.Current.CancellationToken);
        await advancing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A resolver whose discovery runs until it is answered or cancelled. Its continuations run
    /// inside <see cref="Answer"/>, so the push it leads to has happened when that returns.
    /// </summary>
    private sealed class GatedResolver : IContextActionResolver
    {
        private readonly TaskCompletionSource _answer = new();
        private readonly TaskCompletionSource _finish = new();

        /// <summary>Whether a cancelled discovery waits for <see cref="LetFinish"/> before it returns.</summary>
        public bool HoldsOnCancel { get; init; }

        public bool Cancelled { get; private set; }

        public void Answer() => _answer.TrySetResult();

        public void LetFinish() => _finish.TrySetResult();

        public async ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
        {
            try
            {
                await using (cancellationToken.Register(() => _answer.TrySetCanceled(cancellationToken)))
                {
                    await _answer.Task;
                }
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                if (HoldsOnCancel)
                {
                    await _finish.Task;
                }

                throw;
            }

            return [new ContextActionGroupDefinition([new ContextActionDefinition("history.undo", "Undo", "mdi-undo")])];
        }

        public ValueTask<ContextActionOwner?> ResolveByActionIdAsync(ContextTarget target, string actionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<ContextActionOwner?> ResolveByShortcutAsync(ContextTarget target, ContextShortcutDefinition shortcut, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
