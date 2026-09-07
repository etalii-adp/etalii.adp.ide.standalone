using EtAlii.Adp.Context;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The broadcaster turns a history change into one project-actions push. Driven with a fake
/// store it can raise <c>Changed</c> on by hand, a resolver it can count and make throw, and a
/// selection store that records the pushes (diagram-undo-redo Requirement 5.3).
/// </summary>
public class HistoryActionsBroadcasterTests
{
    private const string Root = @"C:\project";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task OneChange_ResultsInOneDiscoveryAndOnePush()
    {
        // Arrange.
        var store = new HistoryActionsBroadcasterFakeStore();
        var resolver = new HistoryActionsBroadcasterCountingResolver();
        var selection = new HistoryActionsBroadcasterRecordingSelectionStore();
        using var broadcaster = new HistoryActionsBroadcaster(store, resolver, selection);

        store.Raise(Root);

        // Act and assert, step by step.
        await WaitUntilAsync(() => selection.Pushes.Count >= 1);
        Assert.Single(selection.Pushes);
        Assert.Equal(Root, selection.Pushes[0].RootPath);
        Assert.Equal(1, resolver.DiscoverCount);
    }

    [Fact]
    public async Task ABurstForOneProject_CollapsesIntoOnePush()
    {
        // Arrange.
        var store = new HistoryActionsBroadcasterFakeStore();
        var resolver = new HistoryActionsBroadcasterCountingResolver();
        var selection = new HistoryActionsBroadcasterRecordingSelectionStore();
        using var broadcaster = new HistoryActionsBroadcaster(store, resolver, selection);

        // Several changes within the coalescing window - a redo that touched several entries.
        store.Raise(Root);
        store.Raise(Root);
        store.Raise(Root);

        // Act and assert, step by step.
        await WaitUntilAsync(() => selection.Pushes.Count >= 1);
        // Give any second push its chance to arrive before asserting there is none.
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Single(selection.Pushes);
        Assert.Equal(1, resolver.DiscoverCount);
    }

    [Fact]
    public async Task ChangesInTwoProjects_EachGetTheirOwnPush()
    {
        // Arrange.
        const string other = @"C:\other";
        var store = new HistoryActionsBroadcasterFakeStore();
        var resolver = new HistoryActionsBroadcasterCountingResolver();
        var selection = new HistoryActionsBroadcasterRecordingSelectionStore();
        using var broadcaster = new HistoryActionsBroadcaster(store, resolver, selection);

        store.Raise(Root);
        store.Raise(other);

        // Act and assert, step by step.
        await WaitUntilAsync(() => selection.Pushes.Count >= 2);
        Assert.Equal(2, selection.Pushes.Count);
        Assert.Contains(selection.Pushes, push => push.RootPath == Root);
        Assert.Contains(selection.Pushes, push => push.RootPath == other);
    }

    [Fact]
    public async Task AThrowingResolver_PushesNothingAndDoesNotEscape()
    {
        // Arrange.
        var store = new HistoryActionsBroadcasterFakeStore();
        var resolver = new HistoryActionsBroadcasterCountingResolver { Throw = true };
        var selection = new HistoryActionsBroadcasterRecordingSelectionStore();
        using var broadcaster = new HistoryActionsBroadcaster(store, resolver, selection);

        store.Raise(Root);

        // Act and assert, step by step.
        // The discovery runs on a timer thread, so wait for it to have run rather than for a
        // fixed span. This waited 200ms and asserted the count afterwards, which is a race the
        // machine wins whenever it is busy: the assertion failed in two of three full-suite
        // runs while passing three of three in isolation, because a loaded machine had not got
        // to the timer thread yet. Waiting on the observable fact is deterministic; waiting on
        // a duration is a bet on the scheduler.
        await WaitUntilAsync(() => resolver.DiscoverCount >= 1);

        // The negative cannot be waited for, and does not need to be: once the discovery has
        // run and thrown, a push either happened or never will.
        Assert.Empty(selection.Pushes);
        Assert.Equal(1, resolver.DiscoverCount);
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
