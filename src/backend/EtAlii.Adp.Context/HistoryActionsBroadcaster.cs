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
    private readonly Debouncer<string> _pending;

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
        // The history store reports each project by its normalized root, so its roots compare as
        // the file system compares names, as a ProjectRoot does (code-structure FR-059).
        _pending = new Debouncer<string>(CoalesceWindow, Broadcast, time, PathContainment.NameComparer);
        _historyStacks.Changed += OnHistoryChanged;
    }

    // Coalesce: one discovery-and-push per project, the window starting over on every change.
    // A short window is enough to fold a handler that records several commands.
    private void OnHistoryChanged(object? sender, HistoryChangedEventArgs args) => _pending.Schedule(args.RootPath);

    private void Broadcast(string rootPath)
    {
        try
        {
            var target = ProjectTarget(rootPath);
            var actions = _actionResolver.DiscoverAsync(target, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            _selectionStore.PushProjectActions(ProjectRoot.From(rootPath), actions);
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

    /// <summary>
    /// Nothing is left to start: the constructor subscribed. The broadcaster is a hosted service so
    /// that the host constructs it as it starts, before the first request (code-structure FR-064).
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        _historyStacks.Changed -= OnHistoryChanged;
        _pending.Dispose();
    }
}
