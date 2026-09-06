using System.Threading.Channels;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Projects;
using Grpc.Core;
using Serilog;

namespace EtAlii.Adp.Backend.Hierarchy;

public sealed class HierarchyService : EtAlii.Adp.HierarchyService.HierarchyServiceBase
{
    private static readonly TimeSpan RootRecoveryPollInterval = TimeSpan.FromSeconds(2);

    private static readonly ILogger _logger = Log.ForContext<HierarchyService>();

    private readonly IProjectStore _projectStore;
    private readonly IHierarchyModelStore _hierarchyModelStore;

    public HierarchyService(IProjectStore projectStore, IHierarchyModelStore hierarchyModelStore)
    {
        _projectStore = projectStore;
        _hierarchyModelStore = hierarchyModelStore;
    }

    public override Task<ListEntriesResponse> ListEntries(ListEntriesRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        if (!ProjectRootResolver.TryResolve(_projectStore, userId, request.ProjectId, out var rootPath, out var error))
        {
            // The refusal reason otherwise reaches only the one client that asked.
            _logger.Warning("Refused to list entries for {UserId} on project {ProjectId}: {Reason}", userId, request.ProjectId, error);
            return Task.FromResult(new ListEntriesResponse { Error = new ListEntriesError { Message = error } });
        }

        var model = _hierarchyModelStore.GetOrCreate(request.WatchId, rootPath);
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

    public override async Task WatchHierarchy(
        WatchHierarchyRequest request,
        IServerStreamWriter<HierarchyMessage> responseStream,
        ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        if (!ProjectRootResolver.TryResolve(_projectStore, userId, request.ProjectId, out var rootPath, out var error))
        {
            _logger.Warning("Refused a hierarchy watch for {UserId} on project {ProjectId}: {Reason}", userId, request.ProjectId, error);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, error));
        }

        var watchId = request.WatchId;
        var model = _hierarchyModelStore.GetOrCreate(watchId, rootPath);
        var channel = Channel.CreateUnbounded<HierarchyMessage>();

        void OnEntryChanged(HierarchyEntryChange change) => channel.Writer.TryWrite(new HierarchyMessage { Change = ToProto(change) });
        model.EntryChanged += OnEntryChanged;

        using var recoveryCts = new CancellationTokenSource();

        // One notification per outage, whichever detector saw it first. On Windows the
        // watcher's own Error event reports the root's deletion almost instantly; on Linux,
        // inotify never reports the watched directory's own deletion at all - the watch just
        // goes silent - so the presence poll below is the only detector there (found by the
        // first Linux CI run, where the RootUnavailable push never came). The latch keeps the
        // two from double-announcing the same outage, and resets once the root recovers so a
        // second outage announces again.
        var rootLost = 0;

        var watcher = CreateWatcher(rootPath, model, HandleRootLost);
        _hierarchyModelStore.AttachWatcher(watchId, watcher);
        _ = WatchRootPresenceAsync(rootPath, HandleRootLost, recoveryCts.Token);
        // Information: a watch is a long-lived resource with a file system watcher behind it,
        // so its open and close are the pair to look for when one is suspected of leaking.
        _logger.Information(
            "Watching {RootPath} for project {ProjectId} on watch {WatchId}",
            rootPath,
            request.ProjectId,
            watchId);

        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(message, context.CancellationToken);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Expected: the client closed the stream (navigated away, reloaded, or the
            // workspace shell unmounted) - not a real error, so don't let it surface as one.
            _logger.Debug("Watch {WatchId} was closed by the client", watchId);
        }
        finally
        {
            model.EntryChanged -= OnEntryChanged;
            await recoveryCts.CancelAsync();
            _hierarchyModelStore.Remove(watchId);
            _logger.Information("Stopped watching {RootPath} on watch {WatchId}", rootPath, watchId);
        }

        return;

        async Task RecoverAsync()
        {
            // ReSharper disable once AccessToDisposedClosure
            // Reason: This works.
            await WaitForRootRecoveryAsync(rootPath, model, recoveryCts.Token);
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

    private RootFolderWatcher CreateWatcher(string rootPath, HierarchyModel model, Action<string> onRootLost)
    {
        return new RootFolderWatcher(
            rootPath,
            onChange: model.OnWatcherEvent,
            onError: ex =>
            {
                if (ex is InternalBufferOverflowException)
                {
                    // Changes arrived faster than the watcher's buffer could hold. Nothing is
                    // lost - the tree is rebuilt - but it means changes were missed in between.
                    _logger.Warning(
                        ex,
                        "The watcher for {RootPath} overflowed its buffer; rebuilding the tree from disk",
                        rootPath);
                    model.Reconcile();
                    return;
                }

                _logger.Error(ex, "The watcher for {RootPath} failed; waiting for the folder to come back", rootPath);
                onRootLost(ex.Message);
            });
    }

    /// <summary>
    /// The platform-independent root-vanish detector: a low-frequency existence poll on the
    /// same cadence the recovery wait already uses. Windows' watcher reports the root's own
    /// deletion through its Error event; Linux's inotify-based watcher does not - it simply
    /// goes quiet - so without this poll a deleted root was never announced there at all.
    /// </summary>
    private static async Task WatchRootPresenceAsync(string rootPath, Action<string> onRootLost, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(RootRecoveryPollInterval, cancellationToken);
                if (!Directory.Exists(rootPath))
                {
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
                await Task.Delay(RootRecoveryPollInterval, cancellationToken);
            }

            _logger.Information("{RootPath} is back; rebuilding the tree from disk", rootPath);
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
            DiagramState = (EtAlii.Adp.EntryDiagramState)node.DiagramState,
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
                ? new EntryUpdated { EntryId = up.EntryId, HasChildren = up.HasChildren, ParentId = up.ParentId ?? default, DiagramState = (EtAlii.Adp.EntryDiagramState)up.DiagramState }
                : new EntryUpdated { EntryId = up.EntryId, HasChildren = up.HasChildren, DiagramState = (EtAlii.Adp.EntryDiagramState)up.DiagramState },
        },
        HierarchyRootUnavailable u => new HierarchyChange { RootUnavailable = new RootUnavailable { Message = u.Message } },
        _ => throw new ArgumentOutOfRangeException(nameof(change)),
    };
}
