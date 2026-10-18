using System.Threading.Channels;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Sessions;
using Grpc.Core;
using Serilog;

namespace EtAlii.Adp.Hierarchy;

public sealed class HierarchyService : Wire.HierarchyService.HierarchyServiceBase
{
    /// <summary>How often a watch looks whether its root is still there, and a lost root whether it is back.</summary>
    internal static readonly TimeSpan RootRecoveryPollInterval = TimeSpan.FromSeconds(2);

    private static readonly ILogger _logger = Log.ForContext<HierarchyService>();

    private readonly IProjectStore _projectStore;
    private readonly IHierarchyModelStore _hierarchyModelStore;
    private readonly ConnectionSessions _sessions;
    private readonly ProjectFileEvents _events;
    private readonly TimeProvider _time;

    /// <summary>
    /// Awaited by the presence poll between finding the root gone and announcing it, so a guard
    /// can hold the poll at exactly that point while the watch closes around it. Settable for
    /// guards only; production never sets it.
    /// </summary>
    internal Func<Task>? RootFoundMissing { get; init; }

    /// <param name="projectStore">The projects a watch is asked for by id.</param>
    /// <param name="hierarchyModelStore">One model per watch.</param>
    /// <param name="events">The file events of each project root.</param>
    /// <param name="sessions">The scope a watch on a connection of its own holds what it acquires in.</param>
    /// <param name="time">The clock the presence and recovery polls wait on; the system's when not given.</param>
    public HierarchyService(IProjectStore projectStore, IHierarchyModelStore hierarchyModelStore, ProjectFileEvents events, ConnectionSessions sessions, TimeProvider? time = null)
    {
        _projectStore = projectStore;
        _hierarchyModelStore = hierarchyModelStore;
        _sessions = sessions;
        _events = events;
        _time = time ?? TimeProvider.System;
    }

    public override Task<ListEntriesResponse> ListEntries(ListEntriesRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        if (!ProjectRootResolver.TryResolve(_projectStore, userId, request.ProjectId, out var root, out var error))
        {
            // The refusal reason otherwise reaches only the one client that asked.
            _logger.Warning("Refused to list entries for {UserId} on project {ProjectId}: {Reason}", userId, request.ProjectId, error);
            return Task.FromResult(new ListEntriesResponse { Error = new ListEntriesError { Message = error } });
        }

        var model = _hierarchyModelStore.GetOrCreate(request.WatchId, root);
        ShortGuid? folderId = request.FolderId is null ? null : (ShortGuid)request.FolderId;
        var children = model.ListChildren(folderId);

        var entries = new Entries();
        entries.Entries_.AddRange(children.Select(ToProto));
        _logger.Debug(
            "Listed {Count} entries under {FolderId} for watch {WatchId}",
            entries.Entries_.Count,
            folderId is { } id ? id.ToString() : "the root",
            request.WatchId);
        return Task.FromResult(new ListEntriesResponse { Entries = entries });
    }

    public override Task WatchHierarchy(
        WatchHierarchyRequest request,
        IServerStreamWriter<HierarchyMessage> responseStream,
        ServerCallContext context) =>
        RunWatchAsync(SessionContext.GetUserId(context), request, responseStream.WriteAsync, context.CancellationToken);

    /// <summary>
    /// The watch on a connection of its own: refuses with an <see cref="RpcException"/> before
    /// anything is acquired, then holds the connection's scope for as long as the watch runs, so
    /// whatever the watch acquires is released when it ends.
    /// </summary>
    public async Task RunWatchAsync(
        ShortGuid userId,
        WatchHierarchyRequest request,
        Func<HierarchyMessage, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (!ProjectRootResolver.TryResolve(_projectStore, userId, request.ProjectId, out var root, out var error))
        {
            _logger.Warning("Refused a hierarchy watch for {UserId} on project {ProjectId}: {Reason}", userId, request.ProjectId, error);
            throw new RpcException(new Status(PermanentRefusal.CannotOpen, error));
        }

        using var connection = _sessions.Open(request.WatchId, root);
        await RunWatchAsync(connection, write, cancellationToken);
    }

    /// <summary>
    /// The watch itself, written to whatever carries it: its own <c>WatchHierarchy</c> call, or the
    /// tab's one <c>WorkspaceService.Watch</c> stream (two-tab-connection-wedge Requirement 3.1).
    /// Its claim on the model and its subscription to the project's file events are held in
    /// <paramref name="connection"/>, which releases them, so the watch releases nothing itself
    /// (code-structure FR-067). Runs until <paramref name="cancellationToken"/> ends it.
    /// </summary>
    public async Task RunWatchAsync(
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

        void OnEntryChanged(HierarchyEntryChange change) => channel.Writer.TryWrite(new HierarchyMessage { Change = ToProto(change) });

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
            // The WatchHierarchy call ended; the poll dies with it.
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
            // The WatchHierarchy call ended before the root folder came back; nothing to recover.
            _logger.Debug("Gave up waiting for {RootPath} to come back: the watch ended first", rootPath);
        }
    }

    private static Entry ToProto(EntryNode node)
    {
        var entry = new Entry
        {
            Id = node.Id,
            Name = node.Name,
            Kind = node.IsFolder ? EntryKind.Folder : EntryKind.File,
            Available = node.Available,
            HasChildren = node.HasChildren,
            DiagramState = (Wire.EntryDiagramState)node.DiagramState,
        };
        if (node.ParentId is { } parentId)
        {
            entry.ParentId = parentId;
        }

        return entry;
    }

    private static HierarchyChange ToProto(HierarchyEntryChange change) => change switch
    {
        HierarchyEntryCreated c => new HierarchyChange { Created = new EntryCreated { Entry = ToProto(c.Entry) } },
        HierarchyEntryRemoved r => new HierarchyChange { Removed = new EntryRemoved { EntryId = r.EntryId } },
        HierarchyEntryRenamed rn => new HierarchyChange { Renamed = new EntryRenamed { EntryId = rn.EntryId, NewName = rn.NewName } },
        // A re-parent sets parent_id; an empty id is "now at the root" (unset would read as
        // "parent unchanged", which a root re-parent is not).
        HierarchyEntryUpdated up => new HierarchyChange
        {
            Updated = up.ParentChanged
                ? new EntryUpdated { EntryId = up.EntryId, HasChildren = up.HasChildren, ParentId = up.ParentId ?? default, DiagramState = (Wire.EntryDiagramState)up.DiagramState }
                : new EntryUpdated { EntryId = up.EntryId, HasChildren = up.HasChildren, DiagramState = (Wire.EntryDiagramState)up.DiagramState },
        },
        HierarchyRootUnavailable u => new HierarchyChange { RootUnavailable = new RootUnavailable { Message = u.Message } },
        _ => throw new ArgumentOutOfRangeException(nameof(change)),
    };
}
