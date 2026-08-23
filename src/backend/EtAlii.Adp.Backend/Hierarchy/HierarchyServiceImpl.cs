using System.Threading.Channels;
using EtAlii.Adp.Backend.Projects;
using EtAlii.Adp.Backend.Sessions;
using Grpc.Core;
using Serilog;

namespace EtAlii.Adp.Backend.Hierarchy;

public sealed class HierarchyServiceImpl : HierarchyService.HierarchyServiceBase
{
    private static readonly TimeSpan RootRecoveryPollInterval = TimeSpan.FromSeconds(2);

    private static readonly ILogger _logger = Log.ForContext<HierarchyServiceImpl>();

    private readonly IProjectStore _projectStore;
    private readonly IHierarchyModelStore _hierarchyModelStore;

    public HierarchyServiceImpl(IProjectStore projectStore, IHierarchyModelStore hierarchyModelStore)
    {
        _projectStore = projectStore;
        _hierarchyModelStore = hierarchyModelStore;
    }

    public override Task<ListEntriesResponse> ListEntries(ListEntriesRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        if (!ProjectRootResolver.TryResolve(_projectStore, userId, request.ProjectId, out var rootPath, out var error))
        {
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
            throw new RpcException(new Status(StatusCode.FailedPrecondition, error));
        }

        var watchId = request.WatchId;
        var model = _hierarchyModelStore.GetOrCreate(watchId, rootPath);
        var channel = Channel.CreateUnbounded<HierarchyMessage>();

        void OnEntryChanged(HierarchyEntryChange change) => channel.Writer.TryWrite(new HierarchyMessage { Change = ToProto(change) });
        model.EntryChanged += OnEntryChanged;

        using var recoveryCts = new CancellationTokenSource();
        var watcher = CreateWatcher(rootPath, model, recoveryCts.Token);
        _hierarchyModelStore.AttachWatcher(watchId, watcher);
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
    }

    private RootFolderWatcher CreateWatcher(string rootPath, HierarchyModel model, CancellationToken recoveryToken)
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
                model.NotifyRootUnavailable(ex.Message);
                _ = WaitForRootRecoveryAsync(rootPath, model, recoveryToken);
            });
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
        };
        if (node.ParentId is { } parentId)
        {
            entry.ParentId = parentId;
        }

        return entry;
    }

    private static HierarchyChange ToProto(HierarchyEntryChange change) => change switch
    {
        HierarchyEntryChange.Created c => new HierarchyChange { Created = new EntryCreated { Entry = ToProto(c.Entry) } },
        HierarchyEntryChange.Removed r => new HierarchyChange { Removed = new EntryRemoved { EntryId = r.EntryId } },
        HierarchyEntryChange.Renamed rn => new HierarchyChange { Renamed = new EntryRenamed { EntryId = rn.EntryId, NewName = rn.NewName } },
        HierarchyEntryChange.Updated up => new HierarchyChange { Updated = new EntryUpdated { EntryId = up.EntryId, HasChildren = up.HasChildren } },
        HierarchyEntryChange.RootUnavailable u => new HierarchyChange { RootUnavailable = new RootUnavailable { Message = u.Message } },
        _ => throw new ArgumentOutOfRangeException(nameof(change)),
    };
}
