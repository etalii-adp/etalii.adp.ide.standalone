using System.Threading.Channels;
using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Projects;
using EtAlii.Adp.Backend.Sessions;
using Google.Protobuf;
using Grpc.Core;
using Serilog;

namespace EtAlii.Adp.Backend.Diagrams;

/// <summary>
/// The one entry point for viewing and editing a diagram, whatever its type. It resolves the
/// project, resolves the diagram's type from its <c>.adp</c> file, opens the matching module's
/// <see cref="IDiagramSession"/>, and pumps that session's deltas onto the stream - mapping
/// the module's backend delta records to the contract's proto in the one place that mapping
/// lives. It knows no diagram type (mindmap-diagram Requirement 13.4).
/// </summary>
public sealed class DiagramServiceImpl : DiagramService.DiagramServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<DiagramServiceImpl>();

    private readonly IProjectStore _projectStore;
    private readonly DiagramFileRouter _router;
    private readonly DiagramSessionFactories _sessionFactories;
    private readonly IDiagramViewportRegistry _viewports;
    private readonly IReadOnlyList<Diagram.IDiagramToolboxProvider> _toolboxProviders;

    public DiagramServiceImpl(
        IProjectStore projectStore,
        DiagramFileRouter router,
        DiagramSessionFactories sessionFactories,
        IDiagramViewportRegistry viewports,
        IEnumerable<Diagram.IDiagramToolboxProvider> toolboxProviders)
    {
        _projectStore = projectStore;
        _router = router;
        _sessionFactories = sessionFactories;
        _viewports = viewports;
        _toolboxProviders = [.. toolboxProviders];
    }

    public override async Task Open(OpenDiagramRequest request, IServerStreamWriter<Delta> responseStream, ServerCallContext context)
    {
        var watchId = (ShortGuid)request.WatchId;
        if (!TryResolveBody(request.ProjectId, request.Path, context, out var rootPath, out var bodyPath, out var origin, out var registrationPath))
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "The diagram cannot be opened."));
        }

        var factory = _sessionFactories.Find(origin);
        if (factory is null)
        {
            throw new RpcException(new Status(StatusCode.Unimplemented, $"'{origin}' diagrams cannot be opened yet."));
        }

        await using var session = factory.Open(watchId, rootPath, bodyPath, registrationPath);
        var channel = Channel.CreateUnbounded<Delta>();

        void OnChanged(object? sender, DiagramDeltasEventArgs args)
        {
            foreach (var delta in args.Deltas)
            {
                channel.Writer.TryWrite(ToProto(delta));
            }
        }

        session.Changed += OnChanged;
        _viewports.Register(watchId, bodyPath, session, viewport => Apply(session, viewport, channel));
        _logger.Information("Opened {BodyPath} on watch {WatchId}", bodyPath, watchId);

        try
        {
            foreach (var delta in session.Baseline())
            {
                await responseStream.WriteAsync(ToProto(delta), context.CancellationToken);
            }

            await foreach (var delta in channel.Reader.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(delta, context.CancellationToken);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            _logger.Debug("Diagram stream on watch {WatchId} closed by the client", watchId);
        }
        finally
        {
            session.Changed -= OnChanged;
            _viewports.Remove(watchId, bodyPath);
            _logger.Information("Closed {BodyPath} on watch {WatchId}", bodyPath, watchId);
        }
    }

    public override Task<UpdateViewResponse> UpdateView(UpdateViewRequest request, ServerCallContext context)
    {
        var watchId = (ShortGuid)request.WatchId;
        if (!TryResolveBody(request.ProjectId, request.Path, context, out _, out var bodyPath, out _, out _))
        {
            return Task.FromResult(new UpdateViewResponse { Error = "The diagram is not open." });
        }

        var box = request.View.BoundingBox;
        var viewport = new DiagramViewport(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y);
        return Task.FromResult(_viewports.Report(watchId, bodyPath, viewport)
            ? new UpdateViewResponse()
            : new UpdateViewResponse { Error = "The diagram is not open on this connection." });
    }

    public override async Task<MoveElementResponse> MoveElement(MoveElementRequest request, ServerCallContext context)
    {
        var watchId = (ShortGuid)request.WatchId;
        if (!TryResolveBody(request.ProjectId, request.Path, context, out _, out var bodyPath, out _, out _))
        {
            return new MoveElementResponse { Error = "The diagram is not open." };
        }

        // The unary leg reaches the stream's session the same way UpdateView does - through
        // the registry the two correlated legs share. The module decides what a move means
        // and dispatches it as a command; this method stays type-agnostic.
        var session = _viewports.Find(watchId, bodyPath);
        if (session is null)
        {
            return new MoveElementResponse { Error = "The diagram is not open on this connection." };
        }

        // Which gesture arrived decides which question the session is asked. A position means
        // "put it here"; its absence means "put it under that", which is the only thing this
        // message could say before the field existed.
        var error = request.Position is { } position
            ? await session.MoveElementToAsync(request.ElementId, position.X, position.Y, context.CancellationToken)
            : await session.MoveElementAsync(request.ElementId, request.NewParentId, request.Index, context.CancellationToken);
        return new MoveElementResponse { Error = error };
    }

    public override Task<DescribeToolboxResponse> DescribeToolbox(DescribeToolboxRequest request, ServerCallContext context)
    {
        var response = new DescribeToolboxResponse();
        if (!TryResolveBody(request.ProjectId, request.Path, context, out _, out _, out var origin, out _))
        {
            // Unresolvable is answered with an empty toolbox, the same non-revealing shape an
            // unauthorized DiscoverActions gets: the palette simply has nothing to offer.
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

    private static void Apply(IDiagramSession session, DiagramViewport viewport, Channel<Delta> channel)
    {
        foreach (var delta in session.UpdateView(viewport))
        {
            channel.Writer.TryWrite(ToProto(delta));
        }
    }

    private bool TryResolveBody(
        Contracts.ShortGuid projectId,
        Path path,
        ServerCallContext context,
        out string rootPath,
        out string bodyPath,
        out Diagram.DiagramOrigin origin,
        out string? registrationPath)
    {
        rootPath = "";
        bodyPath = "";
        origin = null!;
        registrationPath = null;

        var userId = SessionContext.GetUserId(context);
        if (!ProjectRootResolver.TryResolve(_projectStore, userId, projectId, out rootPath, out _))
        {
            return false;
        }

        // The client names the .adp file by project-relative path; the backend combines it
        // with the resolved root and refuses anything that escapes the root, so a path from
        // outside the project resolves to nothing (tech.md: no filesystem path crosses the
        // wire, and a client never reaches outside its workspace).
        var adpPath = System.IO.Path.Combine([rootPath, .. path.Segments]);
        var full = System.IO.Path.GetFullPath(adpPath);
        if (!IsInside(rootPath, full) || !File.Exists(full))
        {
            return false;
        }

        // The root is passed so a registration naming a shared body resolves to it, and so a
        // body: header pointing outside the project is refused (c4-diagrams Requirement 2.4).
        // BodyPath is null only when the router had no root to resolve a body: header against,
        // and one was passed above - so this refuses a document that genuinely does not resolve
        // rather than papering over a missing argument.
        if (_router.Route(full, rootPath) is not DiagramRouted { BodyPath: { } resolvedBody } routed)
        {
            return false;
        }

        bodyPath = resolvedBody;
        origin = routed.Definition.Origin;
        registrationPath = routed.RegistrationPath;
        return true;
    }

    private static bool IsInside(string rootPath, string fullPath)
    {
        var root = System.IO.Path.GetFullPath(rootPath)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static Delta ToProto(DiagramDelta delta) => delta switch
    {
        DiagramAddDelta add => new Delta { Add = new Add { Elements = { add.Elements.Select(ToProto) } } },
        DiagramRemoveDelta remove => new Delta { Remove = new Remove { ElementIds = { remove.ElementIds.Select(id => new ElementId { Value = id }) } } },
        DiagramGroupDelta group => new Delta { Group = new Group { SourceElementIds = { group.SourceElementIds.Select(id => new ElementId { Value = id }) }, GroupElement = ToProto(group.GroupElement) } },
        DiagramUngroupDelta ungroup => new Delta { Ungroup = new Ungroup { GroupElementId = new ElementId { Value = ungroup.GroupElementId }, Elements = { ungroup.Elements.Select(ToProto) } } },
        _ => throw new ArgumentOutOfRangeException(nameof(delta)),
    };

    private static Element ToProto(DiagramElement element) => new()
    {
        Id = new ElementId { Value = element.Id },
        Position = new Point2D { X = element.X, Y = element.Y },
        Type = element.Type,
        Payload = new Google.Protobuf.WellKnownTypes.Any
        {
            TypeUrl = element.PayloadTypeUrl,
            Value = ByteString.CopyFrom(element.Payload.Span),
        },
    };
}
