using System.Threading.Channels;
using EtAlii.Adp.Backend.Context;
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
        var store = new FakeStore();
        var resolver = new CountingResolver();
        var selection = new RecordingSelectionStore();
        using var broadcaster = new HistoryActionsBroadcaster(store, resolver, selection);

        store.Raise(Root);

        await WaitUntilAsync(() => selection.Pushes.Count >= 1);
        Assert.Single(selection.Pushes);
        Assert.Equal(Root, selection.Pushes[0].RootPath);
        Assert.Equal(1, resolver.DiscoverCount);
    }

    [Fact]
    public async Task ABurstForOneProject_CollapsesIntoOnePush()
    {
        var store = new FakeStore();
        var resolver = new CountingResolver();
        var selection = new RecordingSelectionStore();
        using var broadcaster = new HistoryActionsBroadcaster(store, resolver, selection);

        // Several changes within the coalescing window - a redo that touched several entries.
        store.Raise(Root);
        store.Raise(Root);
        store.Raise(Root);

        await WaitUntilAsync(() => selection.Pushes.Count >= 1);
        // Give any second push its chance to arrive before asserting there is none.
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Single(selection.Pushes);
        Assert.Equal(1, resolver.DiscoverCount);
    }

    [Fact]
    public async Task ChangesInTwoProjects_EachGetTheirOwnPush()
    {
        const string other = @"C:\other";
        var store = new FakeStore();
        var resolver = new CountingResolver();
        var selection = new RecordingSelectionStore();
        using var broadcaster = new HistoryActionsBroadcaster(store, resolver, selection);

        store.Raise(Root);
        store.Raise(other);

        await WaitUntilAsync(() => selection.Pushes.Count >= 2);
        Assert.Equal(2, selection.Pushes.Count);
        Assert.Contains(selection.Pushes, push => push.RootPath == Root);
        Assert.Contains(selection.Pushes, push => push.RootPath == other);
    }

    [Fact]
    public async Task AThrowingResolver_PushesNothingAndDoesNotEscape()
    {
        var store = new FakeStore();
        var resolver = new CountingResolver { Throw = true };
        var selection = new RecordingSelectionStore();
        using var broadcaster = new HistoryActionsBroadcaster(store, resolver, selection);

        store.Raise(Root);

        // The discovery runs on a timer thread; wait past the window, then assert it left no
        // push behind and did not take the process down with it.
        await Task.Delay(200, TestContext.Current.CancellationToken);
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

    /// <summary>A store whose <c>Changed</c> the test raises directly; the broadcaster never calls the rest.</summary>
    private sealed class FakeStore : IHistoryStackStore
    {
        public event EventHandler<HistoryChangedEventArgs>? Changed;

        public void Raise(string rootPath) => Changed?.Invoke(this, new HistoryChangedEventArgs(rootPath));

        public IHistoryStack Get(string rootPath) => throw new NotSupportedException();

        public void Retain(string rootPath) => throw new NotSupportedException();

        public void Release(string rootPath) => throw new NotSupportedException();
    }

    private sealed class CountingResolver : IContextActionResolver
    {
        private int _discoverCount;

        public bool Throw { get; init; }

        public int DiscoverCount => Volatile.Read(ref _discoverCount);

        public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _discoverCount);
            if (Throw)
            {
                throw new InvalidOperationException("boom");
            }

            var group = new ContextActionGroupDefinition([new ContextActionDefinition("history.undo", "Undo", "mdi-undo")]);
            return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([group]);
        }

        // The broadcaster only discovers; these two are never reached from it.
        public ValueTask<ContextActionOwner?> ResolveByActionIdAsync(ContextTarget target, string actionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<ContextActionOwner?> ResolveByShortcutAsync(ContextTarget target, ContextShortcutDefinition shortcut, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>Records the project-actions pushes; every other member is unreached by the broadcaster.</summary>
    private sealed class RecordingSelectionStore : IContextSelectionStore
    {
        private readonly object _gate = new();
        private readonly List<(string RootPath, IReadOnlyList<ContextActionGroupDefinition> Actions)> _pushes = [];

        public IReadOnlyList<(string RootPath, IReadOnlyList<ContextActionGroupDefinition> Actions)> Pushes
        {
            get
            {
                lock (_gate)
                {
                    return _pushes.ToList();
                }
            }
        }

        public void PushProjectActions(string rootPath, IReadOnlyList<ContextActionGroupDefinition> actions)
        {
            lock (_gate)
            {
                _pushes.Add((rootPath, actions));
            }
        }

        public void Register(ShortGuid watchId, string rootPath, ChannelWriter<ContextMessage> writer, IReadOnlyList<ContextActionGroupDefinition> rootActions, IReadOnlyList<ContextActionGroupDefinition> projectActions, ProjectProblems problems) => throw new NotSupportedException();

        public void PushProblems(string rootPath, ProjectProblems problems) => throw new NotSupportedException();

        public void Remove(ShortGuid watchId) => throw new NotSupportedException();

        public ContextSelectionRecord Get(ShortGuid watchId) => throw new NotSupportedException();

        public void Set(ShortGuid watchId, string rootPath, ContextSelectionRecord record, ContextRediscovery rediscover) => throw new NotSupportedException();

        public void Clear(ShortGuid watchId) => throw new NotSupportedException();

        public void Refresh(ShortGuid watchId) => throw new NotSupportedException();

        public void PushTransient(ShortGuid watchId, ContextSelectionRecord record) => throw new NotSupportedException();

        public void UpdateFromTrack(ShortGuid watchId, int levelIndex, IReadOnlyList<string>? newRelativePath) => throw new NotSupportedException();
    }
}
