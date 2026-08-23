using System.Threading.Channels;
using EtAlii.Adp.Backend.Projects;
using EtAlii.Adp.Backend.Sessions;
using Grpc.Core;
using Serilog;

namespace EtAlii.Adp.Backend.Context;

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
public sealed partial class ContextServiceImpl : ContextService.ContextServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<ContextServiceImpl>();

    private readonly IProjectStore _projectStore;
    private readonly IContextSelectionStore _selectionStore;
    private readonly ContextSelectionResolver _selectionResolver;
    private readonly IContextActionResolver _contextActionResolver;
    private readonly IContextInteractionStore _contextInteractionStore;

    public ContextServiceImpl(
        IProjectStore projectStore,
        IContextSelectionStore selectionStore,
        ContextSelectionResolver selectionResolver,
        IContextActionResolver contextActionResolver,
        IContextInteractionStore contextInteractionStore)
    {
        _projectStore = projectStore;
        _selectionStore = selectionStore;
        _selectionResolver = selectionResolver;
        _contextActionResolver = contextActionResolver;
        _contextInteractionStore = contextInteractionStore;
    }

    public override async Task<SelectResponse> Select(SelectRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        if (!ProjectRootResolver.TryResolve(_projectStore, userId, request.ProjectId, out var rootPath, out var error))
        {
            return new SelectResponse { Error = error };
        }

        var watchId = (ShortGuid)request.WatchId;
        if (request.Selection is null)
        {
            _logger.Debug("Selection cleared on watch {WatchId}", watchId);
            _selectionStore.Clear(watchId);
            return new SelectResponse();
        }

        var resolution = await _selectionResolver.ResolveChainAsync(watchId, rootPath, request.Selection, context.CancellationToken);
        if (resolution is not ChainResolution.Resolved resolved)
        {
            // Unauthorized, unknown, gone, or malformed: one answer for all of them, and
            // the current selection stays exactly as it was.
            var reason = ((ChainResolution.Rejected)resolution).Reason;
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
            _selectionStore.Set(watchId, rootPath, record, WithActionsAsync);
        }

        return new SelectResponse();
    }

    public override async Task Watch(WatchContextRequest request, IServerStreamWriter<ContextMessage> responseStream, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        if (!ProjectRootResolver.TryResolve(_projectStore, userId, request.ProjectId, out var rootPath, out var error))
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, error));
        }

        var watchId = (ShortGuid)request.WatchId;
        var channel = Channel.CreateUnbounded<ContextMessage>();

        // What applies when nothing is selected: the project root's actions, discovered once
        // here and carried on every "nothing selected" message for this connection.
        var rootActions = await _contextActionResolver.DiscoverAsync(RootTarget(rootPath, watchId), context.CancellationToken);

        // Registering writes the baseline first, so a late subscriber is consistent
        // before anything else can arrive.
        _selectionStore.Register(watchId, channel.Writer, rootActions);
        _contextInteractionStore.Register(watchId, channel.Writer);
        _logger.Information(
            "Context stream open on watch {WatchId} for project {ProjectId}, with {GroupCount} root action groups",
            watchId,
            request.ProjectId,
            rootActions.Count);

        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(message, context.CancellationToken);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Expected: the client closed the stream - not a real error.
            _logger.Debug("Context stream on watch {WatchId} was closed by the client", watchId);
        }
        finally
        {
            _contextInteractionStore.Remove(watchId);
            _selectionStore.Remove(watchId);
            _logger.Information("Context stream closed on watch {WatchId}", watchId);
        }
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
