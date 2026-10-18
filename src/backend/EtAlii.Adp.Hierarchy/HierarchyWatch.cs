using System.Threading.Channels;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.Sessions;
using Serilog;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// A connection's watch on its project's hierarchy: the model's changes written to whatever carries
/// them, the root's disappearance announced and its return recovered from. Run by
/// <see cref="HierarchyService.WatchHierarchy"/> on a call of its own and by the tab's one
/// <c>WorkspaceService.Watch</c> stream alike (two-tab-connection-wedge Requirement 3.1), so neither
/// gRPC service calls the other (code-structure FR-067).
/// </summary>
public sealed class HierarchyWatch
{
    /// <summary>How often a watch looks whether its root is still there, and a lost root whether it is back.</summary>
    internal static readonly TimeSpan RootRecoveryPollInterval = TimeSpan.FromSeconds(2);

    private static readonly ILogger _logger = Log.ForContext<HierarchyWatch>();

    private readonly IHierarchyModelStore _hierarchyModelStore;
    private readonly ProjectFileEvents _events;
    private readonly TimeProvider _time;

    /// <summary>
    /// Awaited by the presence poll between finding the root gone and announcing it, so a guard
    /// can hold the poll at exactly that point while the watch closes around it. Settable for
    /// guards only; production never sets it.
    /// </summary>
    internal Func<Task>? RootFoundMissing { get; init; }

    /// <param name="hierarchyModelStore">One model per watch.</param>
    /// <param name="events">The file events of each project root.</param>
    /// <param name="time">The clock the presence and recovery polls wait on; the system's when not given.</param>
    public HierarchyWatch(IHierarchyModelStore hierarchyModelStore, ProjectFileEvents events, TimeProvider? time = null)
    {
        _hierarchyModelStore = hierarchyModelStore;
        _events = events;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Runs the watch until <paramref name="cancellationToken"/> ends it. Its claim on the model and
    /// its subscription to the project's file events are held in <paramref name="connection"/>,
    /// which releases them, so the watch releases nothing itself (code-structure FR-067).
    /// </summary>
    public async Task RunAsync(
        ConnectionSession connection,
        Func<HierarchyMessage, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var watchId = connection.WatchId;
        var root = connection.Root;
        var model = connection.Hold(() => _hierarchyModelStore.Claim(watchId, root)).Model;

        // The watcher and the presence checks work on the folder itself.
        var rootPath = root.FullPath;
        var channel = Channel.CreateUnbounded<HierarchyMessage>();

        model.EntryChanged += OnEntryChanged;

        using var recoveryCts = new CancellationTokenSource();

        // Read once, here, and never through the source again. CancelAsync in the finally asks the
        // presence poll to stop but does not wait for it, so a poll already past its delay can still
        // announce an outage after the using has disposed the source - and the source's Token
        // throws once disposed, faulting a recovery nobody awaits. The token itself outlives the
        // dispose and is cancelled before it, so a late recovery gives up at its first wait instead.
        var recoveryToken = recoveryCts.Token;

        // One notification per outage, whichever detector saw it first. On Windows the
        // watcher's own Error event (heard as Lost, with the root gone) reports the root's deletion almost instantly; on Linux,
        // inotify never reports the watched directory's own deletion at all - the watch just
        // goes silent - so the presence poll below is the only detector there (found by the
        // first Linux CI run, where the RootUnavailable push never came). The latch keeps the
        // two from double-announcing the same outage, and resets once the root recovers so a
        // second outage announces again.
        var rootLost = 0;

        connection.Hold(() => Subscribe(rootPath, model, HandleRootLost));
        _ = WatchRootPresenceAsync(rootPath, HandleRootLost, recoveryToken);
        // Information: a watch is a long-lived resource with a subscription to the root's one
        // watcher behind it, so its open and close are the pair to look for when one is suspected
        // of leaking.
        _logger.Information(
            "Watching {RootPath} on watch {WatchId}",
            rootPath,
            watchId);

        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
            {
                await write(message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected: the client closed the stream (navigated away, reloaded, or the
            // workspace shell unmounted) - not a real error, so don't let it surface as one.
            _logger.Debug("Watch {WatchId} was closed by the client", watchId);
        }
        finally
        {
            model.EntryChanged -= OnEntryChanged;
            await recoveryCts.CancelAsync();
            _logger.Information("Stopped watching {RootPath} on watch {WatchId}", rootPath, watchId);
        }

        return;

        void OnEntryChanged(HierarchyEntryChange change) => channel.Writer.TryWrite(new HierarchyMessage { Change = HierarchyWire.ToProto(change) });

        async Task RecoverAsync()
        {
            await WaitForRootRecoveryAsync(rootPath, model, recoveryToken);
            Interlocked.Exchange(ref rootLost, 0);
        }

        void HandleRootLost(string message)
        {
            if (Interlocked.Exchange(ref rootLost, 1) == 1)
            {
                return;
            }

            model.NotifyRootUnavailable(message);
            _ = RecoverAsync();
        }
    }

    private IDisposable Subscribe(string rootPath, HierarchyModel model, Action<string> onRootLost)
    {
        return _events.Subscribe(rootPath, heard =>
        {
            if (heard.Kind != FileEventKind.Lost)
            {
                model.OnFileEvent(heard);
                return;
            }

            if (Directory.Exists(rootPath))
            {
                // Changes arrived faster than the watcher's buffer could hold. Nothing is lost -
                // the tree is rebuilt - but it means changes were missed in between.
                _logger.Warning("The watcher for {RootPath} lost events; rebuilding the tree from disk", rootPath);
                model.Reconcile();
                return;
            }

            _logger.Error("The watcher for {RootPath} failed; waiting for the folder to come back", rootPath);
            onRootLost("The project's root folder is no longer accessible.");
        });
    }

    /// <summary>
    /// The platform-independent root-vanish detector: a low-frequency existence poll on the
    /// same cadence the recovery wait already uses. Windows' watcher reports the root's own
    /// deletion through its Error event; Linux's inotify-based watcher does not - it simply
    /// goes quiet - so without this poll a deleted root was never announced there at all.
    /// </summary>
    private async Task WatchRootPresenceAsync(string rootPath, Action<string> onRootLost, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(RootRecoveryPollInterval, _time, cancellationToken);
                if (!Directory.Exists(rootPath))
                {
                    if (RootFoundMissing is { } rootFoundMissing)
                    {
                        await rootFoundMissing();
                    }

                    onRootLost("The project's root folder is no longer accessible.");
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            // The watch ended; the poll dies with it.
        }
    }

    private async Task WaitForRootRecoveryAsync(string rootPath, HierarchyModel model, CancellationToken cancellationToken)
    {
        try
        {
            while (!Directory.Exists(rootPath))
            {
                await Task.Delay(RootRecoveryPollInterval, _time, cancellationToken);
            }

            _logger.Information("{RootPath} is back; watching it anew and rebuilding the tree from disk", rootPath);

            // The watcher of a root that went away hears nothing any more - on Linux without a word.
            _events.WatchAgain(rootPath);
            model.Reconcile();
        }
        catch (OperationCanceledException)
        {
            // The watch ended before the root folder came back; nothing to recover.
            _logger.Debug("Gave up waiting for {RootPath} to come back: the watch ended first", rootPath);
        }
    }
}
