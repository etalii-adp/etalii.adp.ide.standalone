using System.Collections.Concurrent;
using EtAlii.Adp.Backend.History;
using Serilog;

namespace EtAlii.Adp.Backend.Context;

/// <summary>
/// The one thing that turns a history change into a push: it subscribes once to the history
/// store, and on each change discovers the project's actions and sends them to that project's
/// connections (diagram-undo-redo Requirement 5.3). A burst for one project collapses into a
/// single discovery-and-push, so a redo of several entries is not a storm of pushes.
/// </summary>
public sealed class HistoryActionsBroadcaster : IDisposable
{
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(50);
    private static readonly ILogger _logger = Log.ForContext<HistoryActionsBroadcaster>();

    private readonly IHistoryStackStore _historyStacks;
    private readonly IContextActionResolver _actionResolver;
    private readonly IContextSelectionStore _selectionStore;
    private readonly ConcurrentDictionary<string, Timer> _pending = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public HistoryActionsBroadcaster(
        IHistoryStackStore historyStacks,
        IContextActionResolver actionResolver,
        IContextSelectionStore selectionStore)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(actionResolver);
        ArgumentNullException.ThrowIfNull(selectionStore);
        _historyStacks = historyStacks;
        _actionResolver = actionResolver;
        _selectionStore = selectionStore;
        _historyStacks.Changed += OnHistoryChanged;
    }

    private void OnHistoryChanged(object? sender, HistoryChangedEventArgs args)
    {
        // Coalesce: schedule one discovery-and-push for this project, replacing any already
        // pending. A short window is enough to fold a handler that records several commands.
        var timer = new Timer(_ => Broadcast(args.RootPath), null, CoalesceWindow, Timeout.InfiniteTimeSpan);
        _pending.AddOrUpdate(
            args.RootPath,
            timer,
            (_, existing) =>
            {
                existing.Dispose();
                return timer;
            });
    }

    private void Broadcast(string rootPath)
    {
        _pending.TryRemove(rootPath, out var timer);
        timer?.Dispose();
        if (_disposed)
        {
            return;
        }

        try
        {
            var target = ProjectTarget(rootPath);
            var actions = _actionResolver.DiscoverAsync(target, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            _selectionStore.PushProjectActions(rootPath, actions);
        }
        catch (Exception exception)
        {
            // A discovery that failed pushes nothing; the clients keep the availability they
            // last had rather than losing their buttons to a transient fault.
            _logger.Warning(exception, "Could not broadcast project actions for {RootPath}", rootPath);
        }
    }

    /// <summary>The project itself as an action target - what the project-scope providers resolve against.</summary>
    internal static ContextTarget ProjectTarget(string rootPath) =>
        new(ContextScope.Project, rootPath, IsContainer: true, SourceId: default, RootPath: rootPath);

    public void Dispose()
    {
        _disposed = true;
        _historyStacks.Changed -= OnHistoryChanged;
        foreach (var timer in _pending.Values)
        {
            timer.Dispose();
        }

        _pending.Clear();
    }
}
