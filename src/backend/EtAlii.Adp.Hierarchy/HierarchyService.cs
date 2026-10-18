using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Sessions;
using Grpc.Core;
using Serilog;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The hierarchy's gRPC calls: the listing of a folder, and the watch on a call of its own, which
/// <see cref="HierarchyWatch"/> runs (code-structure FR-067).
/// </summary>
public sealed class HierarchyService : Wire.HierarchyService.HierarchyServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<HierarchyService>();

    private readonly IProjectStore _projectStore;
    private readonly IHierarchyModelStore _hierarchyModelStore;
    private readonly ConnectionSessions _sessions;
    private readonly HierarchyWatch _watch;

    /// <param name="projectStore">The projects a call asks for by id.</param>
    /// <param name="hierarchyModelStore">One model per watch.</param>
    /// <param name="sessions">The scope a watch on a call of its own holds what it acquires in.</param>
    /// <param name="watch">What runs the watch.</param>
    public HierarchyService(IProjectStore projectStore, IHierarchyModelStore hierarchyModelStore, ConnectionSessions sessions, HierarchyWatch watch)
    {
        _projectStore = projectStore;
        _hierarchyModelStore = hierarchyModelStore;
        _sessions = sessions;
        _watch = watch;
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
        entries.Entries_.AddRange(children.Select(HierarchyWire.ToProto));
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
    /// The watch on a call of its own: refuses with an <see cref="RpcException"/> before anything
    /// is acquired, then holds the connection's scope for as long as the watch runs, so whatever
    /// the watch acquires is released when it ends.
    /// </summary>
    internal async Task RunWatchAsync(
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
        await _watch.RunAsync(connection, write, cancellationToken);
    }
}
