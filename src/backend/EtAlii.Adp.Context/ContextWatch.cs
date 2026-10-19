using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Sessions;
using Serilog;

namespace EtAlii.Adp.Context;

/// <summary>
/// A connection's context stream: its baseline, then everything pushed to the connection. Run by
/// <see cref="ContextService.Watch"/> on a call of its own and by the tab's one
/// <c>WorkspaceService.Watch</c> stream alike (two-tab-connection-wedge Requirement 3.1), so neither
/// gRPC service calls the other (code-structure FR-067).
/// </summary>
public sealed class ContextWatch
{
    private static readonly ILogger _logger = Log.ForContext<ContextWatch>();

    private readonly IContextSelectionStore _selectionStore;
    private readonly IContextActionResolver _contextActionResolver;
    private readonly IContextInteractionStore _contextInteractionStore;
    private readonly IContextWatchHooks _watchHooks;

    public ContextWatch(
        IContextSelectionStore selectionStore,
        IContextActionResolver contextActionResolver,
        IContextInteractionStore contextInteractionStore,
        IContextWatchHooks watchHooks)
    {
        _selectionStore = selectionStore;
        _contextActionResolver = contextActionResolver;
        _contextInteractionStore = contextInteractionStore;
        _watchHooks = watchHooks;
    }

    /// <summary>
    /// Runs the stream until <paramref name="cancellationToken"/> ends it. What it registers it
    /// holds in <paramref name="connection"/>, whose scope already holds the project's history, so
    /// it releases nothing itself: a discovery that fails or is cancelled leaves nothing behind once
    /// the scope goes (code-structure FR-023, FR-067). The first message written is the
    /// connection's baseline.
    /// </summary>
    public async Task RunAsync(
        ConnectionSession connection,
        Func<ContextMessage, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var watchId = connection.WatchId;
        var root = connection.Root;
        // Bounded: a tab that stops reading ends the stream rather than growing it (B-28).
        var channel = StreamBound.Ending<ContextMessage>($"Context stream on watch {watchId}");

        // What applies when nothing is selected: the project root's actions, discovered once
        // here and carried on every "nothing selected" message for this connection.
        var rootActions = await _contextActionResolver.DiscoverAsync(RootTarget(root, watchId), cancellationToken);

        try
        {
            // The project's own actions - undo and redo - which belong to the project rather than
            // the selection and travel on their own message (diagram-undo-redo Deviation 1). The
            // project's history they act on is held by the connection's scope.
            var projectActions = await _contextActionResolver.DiscoverAsync(
                HistoryActionsBroadcaster.ProjectTarget(root), cancellationToken);

            // Registering writes the baseline first, so a late subscriber is consistent
            // before anything else can arrive. The problems are read by the registration
            // itself, once the connection can be pushed to: only the broadcaster knows both stores.
            connection.Hold(() => _selectionStore.Register(watchId, root, channel.Writer, rootActions, projectActions, () => _watchHooks.CurrentFor(root)));
            connection.Hold(() => _contextInteractionStore.Register(watchId, channel.Writer));

            // An open project's problems stay current without anyone asking: the connection's scope
            // tracks them, and here the project's validation moves to the front of the background
            // queue - joining it when the project was never validated - because its freshness is
            // the one the user can see (errors-and-warnings-panel Requirements 4.2, 5). After the
            // registration, so that validation's answer arrives as a push behind the baseline.
            _watchHooks.ProjectWatched(root);

            _logger.Information(
                "Context stream open on watch {WatchId} for project {ProjectRoot}, with {GroupCount} root action groups",
                watchId,
                root.FullPath,
                rootActions.Count);

            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
            {
                await write(message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected: the client closed the stream - not a real error.
            _logger.Debug("Context stream on watch {WatchId} was closed by the client", watchId);
        }
        finally
        {
            _logger.Information("Context stream closed on watch {WatchId}", watchId);
        }
    }

    /// <summary>
    /// The project root as an action target. It has no entry id - it is the folder the
    /// entries live in, not an entry - so its source id is the empty guid.
    /// </summary>
    internal static ContextTarget RootTarget(ProjectRoot root, ShortGuid watchId = default) =>
        new(ContextScope.Hierarchy, root.FullPath, IsContainer: true, SourceId: default, Root: root, WatchId: watchId);
}
