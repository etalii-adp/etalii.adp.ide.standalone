using EtAlii.Adp.Diagram;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Projects;
using EtAlii.Adp.Sessions;
using Grpc.Core;
using Serilog;
using Path = EtAlii.Adp.Documents.Wire.Path;
namespace EtAlii.Adp.Workspace;

/// <summary>
/// The one entry point for viewing and editing a diagram, whatever its type. Its <c>Open</c> has
/// <see cref="DiagramPump"/> resolve the project and the diagram's type, open the matching module's
/// <see cref="IDiagramSession"/> and pump that session's deltas onto the stream; its unary calls
/// reach the open session through the viewport registry. It knows no diagram type
/// (mindmap-diagram Requirement 13.4).
/// </summary>
public sealed partial class DiagramService : Diagram.Wire.DiagramService.DiagramServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<DiagramService>();

    private readonly IProjectStore _projectStore;
    private readonly DiagramFileRouter _router;
    private readonly DiagramPump _pump;
    private readonly IReadOnlyList<IDiagramToolboxProvider> _toolboxProviders;

    public DiagramService(
        IProjectStore projectStore,
        DiagramFileRouter router,
        DiagramPump pump,
        IDiagramViewportRegistry viewports,
        IEnumerable<IDiagramToolboxProvider> toolboxProviders)
    {
        _projectStore = projectStore;
        _router = router;
        _pump = pump;
        _viewports = viewports;
        _toolboxProviders = [.. toolboxProviders];
    }

    public override async Task<MoveElementResponse> MoveElement(MoveElementRequest request, ServerCallContext context)
    {
        var watchId = (ShortGuid)request.WatchId;
        if (!TryResolveBody(request.ProjectId, request.Path, SessionContext.GetUserId(context), out var bodyPath, out _))
        {
            _logger.Warning("Refused to move {ElementId} on watch {WatchId}: {Path} does not resolve to an open diagram", request.ElementId, watchId, string.Join('/', request.Path.Segments));
            return new MoveElementResponse { Error = "The diagram is not open." };
        }

        // The unary leg reaches the stream's session the same way UpdateView does - through
        // the registry the two correlated legs share. The module decides what a move means
        // and dispatches it as a command; this method stays type-agnostic.
        var session = _viewports.Find(watchId, bodyPath);
        if (session is null)
        {
            _logger.Warning("Refused to move {ElementId} on {BodyPath}: watch {WatchId} has no open stream for it", request.ElementId, bodyPath, watchId);
            return new MoveElementResponse { Error = "The diagram is not open on this connection." };
        }

        // Which gesture arrived decides which question the session is asked. A position means
        // "put it here"; its absence means "put it under that", which is the only thing this
        // message could say before the field existed.
        var error = request.Position is { } position
            ? await session.MoveElementToAsync(request.ElementId, position.X, position.Y, context.CancellationToken)
            : await session.MoveElementAsync(request.ElementId, request.NewParentId, request.Index, context.CancellationToken);
        if (error.Length > 0)
        {
            // The module said no; its reason otherwise reaches only the one client that asked.
            _logger.Warning("The module refused the move of {ElementId} on {BodyPath}: {Reason}", request.ElementId, bodyPath, error);
        }
        else
        {
            _logger.Debug(
                "Moved {ElementId} on {BodyPath} via {Gesture}",
                request.ElementId,
                bodyPath,
                request.Position is null ? "re-parent" : "position");
        }

        return new MoveElementResponse { Error = error };
    }

    public override Task<DescribeToolboxResponse> DescribeToolbox(DescribeToolboxRequest request, ServerCallContext context)
    {
        var response = new DescribeToolboxResponse();
        if (!TryResolveBody(request.ProjectId, request.Path, SessionContext.GetUserId(context), out _, out var origin))
        {
            // Unresolvable is answered with an empty toolbox, the same non-revealing shape an
            // unauthorized DiscoverActions gets: the palette simply has nothing to offer.
            _logger.Debug("Answering an empty toolbox for {Path}: it does not resolve to a diagram", string.Join('/', request.Path.Segments));
            return Task.FromResult(response);
        }

        var provider = _toolboxProviders.FirstOrDefault(candidate => candidate.Origin == origin);
        if (provider is null)
        {
            _logger.Debug("No toolbox registered for {Origin}; answering with an empty palette", origin);
            return Task.FromResult(response);
        }

        response.Items.AddRange(provider.Items.Select(item => new ToolboxItem
        {
            Id = item.Id,
            Label = item.Label,
            Icon = item.Icon,
            Description = item.Description,
            DropActionId = item.DropActionId,
        }));
        return Task.FromResult(response);
    }

    /// <summary>
    /// The shared first half of every open: the project root, the containment check, and the
    /// file's existence - nothing about which tool.
    /// </summary>
    private bool TryResolveTextFile(
        Documents.Wire.ShortGuid projectId,
        Path path,
        ShortGuid userId,
        out ProjectPath file) =>
        ProjectTextFile.TryResolve(_projectStore, projectId, path, userId, out file);
}
