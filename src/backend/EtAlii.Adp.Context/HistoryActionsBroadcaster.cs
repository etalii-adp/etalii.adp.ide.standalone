using System.Collections.Concurrent;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace EtAlii.Adp.Context;

/// <summary>
/// The one thing that turns a history change into a push: it subscribes once to the history
/// store, and on each change discovers the project's actions and sends them to that project's
/// connections (diagram-undo-redo Requirement 5.3). A burst for one project collapses into a
/// single discovery-and-push, so a redo of several entries is not a storm of pushes.
/// </summary>
public sealed class HistoryActionsBroadcaster : IHostedService, IDisposable
{
    internal static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(50);
    private static readonly ILogger _logger = Log.ForContext<HistoryActionsBroadcaster>();

    private readonly IHistoryStackStore _historyStacks;
    private readonly IContextActionResolver _actionResolver;
    private readonly IContextSelectionStore _selectionStore;
    private readonly Debouncer<ProjectRoot> _pending;
    private readonly CancellationTokenSource _stopping = new();
    private bool _isDisposed;

    // The broadcasts under way, keyed by a token each knows and its task does not, so it can remove
    // itself; null for the moment between a broadcast starting and its task being known.
    private readonly ConcurrentDictionary<object, Task?> _broadcasts = new();

    public HistoryActionsBroadcaster(
        IHistoryStackStore historyStacks,
        IContextActionResolver actionResolver,
        IContextSelectionStore selectionStore,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(actionResolver);
        ArgumentNullException.ThrowIfNull(selectionStore);
        ArgumentNullException.ThrowIfNull(time);
        _historyStacks = historyStacks;
        _actionResolver = actionResolver;
        _selectionStore = selectionStore;
        // The history store reports each project by its root, which compares as the file system
        // compares names (code-structure FR-059).
        _pending = new Debouncer<ProjectRoot>(CoalesceWindow, Broadcast, time);
        _historyStacks.Changed += OnHistoryChanged;
    }

    // Coalesce: one discovery-and-push per project, the window starting over on every change.
    // A short window is enough to fold a handler that records several commands.
    private void OnHistoryChanged(object? sender, HistoryChangedEventArgs args) => _pending.Schedule(args.Root);

    // Runs on the clock's timer, which it hands back at once: the discovery is awaited by the
    // broadcast, and the broadcast by StopAsync, rather than waited on here (code-structure FR-070).
    private void Broadcast(ProjectRoot root)
    {
        var key = new object();
        _broadcasts[key] = null;
        var broadcast = BroadcastAsync(root, key);
        _broadcasts.TryUpdate(key, broadcast, null);
    }

    /// <summary>Never throws: a broadcast that fails is logged and pushes nothing.</summary>
    private async Task BroadcastAsync(ProjectRoot root, object key)
    {
        try
        {
            var actions = await _actionResolver.DiscoverAsync(ProjectTarget(root), _stopping.Token);
            _selectionStore.PushProjectActions(root, actions);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The host is stopping; nobody is left to push to.
        }
        catch (Exception exception)
        {
            // A discovery that failed pushes nothing; the clients keep the availability they
            // last had rather than losing their buttons to a transient fault.
            _logger.Warning(exception, "Could not broadcast project actions for {RootPath}", root.FullPath);
        }
        finally
        {
            _broadcasts.TryRemove(key, out _);
        }
    }

    /// <summary>The project itself as an action target - what the project-scope providers resolve against.</summary>
    internal static ContextTarget ProjectTarget(ProjectRoot root) =>
        new(ContextScope.Project, root.FullPath, IsContainer: true, SourceId: default, Root: root);

    /// <summary>
    /// Nothing is left to start: the constructor subscribed. The broadcaster is a hosted service so
    /// that the host constructs it as it starts, before the first request (code-structure FR-064).
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Starts no broadcast more, cancels the ones under way and waits until they have returned. The
    /// host may have disposed the broadcaster already, which cancelled them.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _pending.Dispose();
        if (!Volatile.Read(ref _isDisposed))
        {
            await _stopping.CancelAsync();
        }

        await Task.WhenAll(_broadcasts.Values.OfType<Task>()).WaitAsync(cancellationToken);
    }

    /// <summary>Safe to call more than once: the host may dispose its services twice.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _historyStacks.Changed -= OnHistoryChanged;
        _pending.Dispose();
        _stopping.Cancel();
        _stopping.Dispose();
    }
}
