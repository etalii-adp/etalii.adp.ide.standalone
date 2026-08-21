using System.Threading.Channels;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Projects;
using EtAlii.Adp.Backend.Sessions;
using Grpc.Core;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Hierarchy;

public sealed partial class HierarchyServiceImpl : HierarchyService.HierarchyServiceBase
{
    private static readonly TimeSpan RootRecoveryPollInterval = TimeSpan.FromSeconds(2);

    private readonly IProjectStore _projectStore;
    private readonly IHierarchyModelStore _hierarchyModelStore;
    private readonly IContextActionResolver _contextActionResolver;
    private readonly IContextInteractionStore _contextInteractionStore;

    public HierarchyServiceImpl(
        IProjectStore projectStore,
        IHierarchyModelStore hierarchyModelStore,
        IContextActionResolver contextActionResolver,
        IContextInteractionStore contextInteractionStore)
    {
        _projectStore = projectStore;
        _hierarchyModelStore = hierarchyModelStore;
        _contextActionResolver = contextActionResolver;
        _contextInteractionStore = contextInteractionStore;
    }

    public override Task<ListEntriesResponse> ListEntries(ListEntriesRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        if (!TryResolveRootPath(userId, request.ProjectId, out var rootPath, out var error))
        {
            return Task.FromResult(new ListEntriesResponse { Error = new ListEntriesError { Message = error } });
        }

        var model = _hierarchyModelStore.GetOrCreate(request.WatchId, rootPath);
        ShortGuid? folderId = request.FolderId is null ? null : (ShortGuid)request.FolderId;
        var children = model.ListChildren(folderId);

        var entries = new Entries();
        entries.Entries_.AddRange(children.Select(ToProto));
        return Task.FromResult(new ListEntriesResponse { Entries = entries });
    }

    public override async Task WatchHierarchy(
        WatchHierarchyRequest request,
        IServerStreamWriter<HierarchyMessage> responseStream,
        ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        if (!TryResolveRootPath(userId, request.ProjectId, out var rootPath, out var error))
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, error));
        }

        var watchId = request.WatchId;
        var model = _hierarchyModelStore.GetOrCreate(watchId, rootPath);
        var channel = Channel.CreateUnbounded<HierarchyMessage>();

        void OnEntryChanged(HierarchyEntryChange change) => channel.Writer.TryWrite(new HierarchyMessage { Change = ToProto(change) });
        model.EntryChanged += OnEntryChanged;

        // The same stream carries backend-initiated context prompts, so an action started
        // by a unary call on this connection can reach this connection - and only it.
        _contextInteractionStore.Register(watchId, channel.Writer);

        using var recoveryCts = new CancellationTokenSource();
        var watcher = CreateWatcher(rootPath, model, recoveryCts.Token);
        _hierarchyModelStore.AttachWatcher(watchId, watcher);

        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(message);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Expected: the client closed the stream (navigated away, reloaded, or the
            // workspace shell unmounted) - not a real error, so don't let it surface as one.
        }
        finally
        {
            model.EntryChanged -= OnEntryChanged;
            recoveryCts.Cancel();
            _contextInteractionStore.Remove(watchId);
            _hierarchyModelStore.Remove(watchId);
        }
    }

    private RootFolderWatcher CreateWatcher(string rootPath, HierarchyModel model, CancellationToken recoveryToken)
    {
        return new RootFolderWatcher(
            rootPath,
            onChange: (changeType, oldPath, newPath) => model.OnWatcherEvent(changeType, oldPath, newPath),
            onError: ex =>
            {
                if (ex is InternalBufferOverflowException)
                {
                    model.Reconcile();
                    return;
                }

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

            model.Reconcile();
        }
        catch (OperationCanceledException)
        {
            // The WatchHierarchy call ended before the root folder came back; nothing to recover.
        }
    }

    private bool TryResolveRootPath(ShortGuid userId, ShortGuid projectId, out string rootPath, out string error)
    {
        var project = _projectStore.List(userId).FirstOrDefault(p => p.Id == projectId);
        if (project is null)
        {
            rootPath = "";
            error = "Project not found.";
            return false;
        }

        var candidatePath = IoPath.Combine(project.Path.Segments.ToArray());
        if (!Directory.Exists(candidatePath))
        {
            rootPath = "";
            error = "The project's root folder is no longer accessible.";
            return false;
        }

        rootPath = candidatePath;
        error = "";
        return true;
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
