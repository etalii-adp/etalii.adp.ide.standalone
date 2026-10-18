using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Sessions;
using Grpc.Core;
using Serilog;

namespace EtAlii.Adp.Context;

/// <summary>
/// Owns what each connection currently has selected, and the one stream a browser
/// client can be pushed anything over. Selections come in as chains of ids the
/// registered resolvers verify; what goes out is the verified chain, per-level detail
/// the backend resolved, and the innermost level's actions - so every consumer on the
/// client reads the same answer without a round trip of its own.
/// </summary>
/// <remarks>
/// Nothing here knows about files, folders or any diagram type: resolution goes through
/// <see cref="ContextSelectionResolver"/> and actions through <see cref="IContextActionResolver"/>.
/// </remarks>
public sealed partial class ContextService : Wire.ContextService.ContextServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<ContextService>();

    private readonly IProjectStore _projectStore;
    private readonly IContextSelectionStore _selectionStore;
    private readonly ContextSelectionResolver _selectionResolver;
    private readonly IContextActionResolver _contextActionResolver;
    private readonly IContextPropertyResolver _contextPropertyResolver;
    private readonly IContextInteractionStore _contextInteractionStore;
    private readonly ConnectionSessions _sessions;
    private readonly ContextWatch _watch;

    public ContextService(
        IProjectStore projectStore,
        IContextSelectionStore selectionStore,
        ContextSelectionResolver selectionResolver,
        IContextActionResolver contextActionResolver,
        IContextPropertyResolver contextPropertyResolver,
        IContextInteractionStore contextInteractionStore,
        ConnectionSessions sessions,
        ContextWatch watch)
    {
        _projectStore = projectStore;
        _selectionStore = selectionStore;
        _selectionResolver = selectionResolver;
        _contextActionResolver = contextActionResolver;
        _contextPropertyResolver = contextPropertyResolver;
        _contextInteractionStore = contextInteractionStore;
        _sessions = sessions;
        _watch = watch;
    }

    public override async Task<SelectResponse> Select(SelectRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        if (!ProjectRootResolver.TryResolve(_projectStore, userId, request.ProjectId, out var root, out var error))
        {
            // The refusal reason otherwise reaches only the one client that asked.
            _logger.Warning("Rejected a selection from {UserId} on project {ProjectId}: {Reason}", userId, request.ProjectId, error);
            return new SelectResponse { Error = error };
        }

        var watchId = (ShortGuid)request.WatchId;
        if (request.Selection is null)
        {
            _logger.Debug("Selection cleared on watch {WatchId}", watchId);
            _selectionStore.Clear(watchId);
            return new SelectResponse();
        }

        var resolution = await _selectionResolver.ResolveChainAsync(watchId, root, request.Selection, context.CancellationToken);
        if (resolution is not ResolvedChain resolved)
        {
            // Unauthorized, unknown, gone, or malformed: one answer for all of them, and
            // the current selection stays exactly as it was.
            var reason = ((RejectedChain)resolution).Reason;
            // The client is told only that it was rejected; the log is the one place the
            // source and id it asked about are kept beside the reason.
            _logger.Warning(
                "Rejected the selection of {Source}/{SelectionId} on watch {WatchId}: {Reason}",
                request.Selection.Source,
                request.Selection.Id,
                watchId,
                reason);
            return new SelectResponse { Error = reason };
        }

        var record = await WithActionsAsync(resolved.Record, context.CancellationToken);
        _logger.Debug(
            "Selected {Path} on watch {WatchId} as {SelectionAction}, with {GroupCount} action groups",
            record.Innermost.Target.ResolvedFullPath,
            watchId,
            record.Action,
            record.Actions.Count);

        if (record.Action == ContextSelectionAction.Preview)
        {
            _selectionStore.PushTransient(watchId, record);
        }
        else
        {
            _selectionStore.Set(watchId, root, record, WithActionsAsync);
        }

        return new SelectResponse();
    }

    public override Task Watch(WatchContextRequest request, IServerStreamWriter<ContextMessage> responseStream, ServerCallContext context) =>
        RunWatchAsync(SessionContext.GetUserId(context), request, responseStream.WriteAsync, context.CancellationToken);

    /// <summary>
    /// The context stream on a call of its own, which <see cref="ContextWatch"/> runs: refuses with
    /// an <see cref="RpcException"/> before anything is acquired, then holds the connection's scope
    /// for as long as the stream runs, so whatever the stream acquires is released when it ends.
    /// </summary>
    internal async Task RunWatchAsync(
        ShortGuid userId,
        WatchContextRequest request,
        Func<ContextMessage, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (!ProjectRootResolver.TryResolve(_projectStore, userId, request.ProjectId, out var root, out var error))
        {
            _logger.Warning("Refused a context stream for {UserId} on project {ProjectId}: {Reason}", userId, request.ProjectId, error);
            throw new RpcException(new Status(PermanentRefusal.CannotOpen, error));
        }

        using var connection = _sessions.Open((ShortGuid)request.WatchId, root);
        await _watch.RunAsync(connection, write, cancellationToken);
    }

    /// <summary>
    /// Discovers the innermost level's actions. A provider failing leaves the selection
    /// itself valid: it is recorded with no actions and the failure is logged.
    /// </summary>
    private async ValueTask<ContextSelectionRecord> WithActionsAsync(ContextSelectionRecord record, CancellationToken cancellationToken)
    {
        try
        {
            var actions = await _contextActionResolver.DiscoverAsync(record.Innermost.Target, cancellationToken);
            return record with { Actions = actions };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "Discovering actions for the current selection failed; recording it without actions");
            return record with { Actions = [] };
        }
    }
}
