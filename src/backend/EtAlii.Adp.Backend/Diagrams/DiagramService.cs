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
public sealed class DiagramService : EtAlii.Adp.DiagramService.DiagramServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<DiagramService>();

    private readonly IProjectStore _projectStore;
    private readonly DiagramFileRouter _router;
    private readonly DiagramSessionFactories _sessionFactories;
    private readonly EditorResolver _editorResolver;
    private readonly EditorSessionFactories _editorSessionFactories;
    private readonly IDiagramViewportRegistry _viewports;
    private readonly IHistoryStackStore _historyStacks;
    private readonly IReadOnlyList<Diagram.IDiagramToolboxProvider> _toolboxProviders;

    public DiagramService(
        IProjectStore projectStore,
        DiagramFileRouter router,
        DiagramSessionFactories sessionFactories,
        EditorResolver editorResolver,
        EditorSessionFactories editorSessionFactories,
        IDiagramViewportRegistry viewports,
        IHistoryStackStore historyStacks,
        IEnumerable<Diagram.IDiagramToolboxProvider> toolboxProviders)
    {
        _historyStacks = historyStacks;
        _projectStore = projectStore;
        _router = router;
        _sessionFactories = sessionFactories;
        _editorResolver = editorResolver;
        _editorSessionFactories = editorSessionFactories;
        _viewports = viewports;
        _toolboxProviders = [.. toolboxProviders];
    }

    public override async Task Open(OpenDiagramRequest request, IServerStreamWriter<Delta> responseStream, ServerCallContext context)
    {
        var watchId = (ShortGuid)request.WatchId;

        // Diagrams first, unconditionally: a file the router claims opens exactly as it
        // always has, and only its NotADiagram answer consults the editor family
        // (modular-text-editors Requirement 5.1). The fallback editor answers for whatever
        // remains, so a file that resolves to nothing at all is a broken deployment, not an
        // ordinary miss (Requirement 3.2).
        IDiagramSession openedSession;
        string bodyPath;
        if (request.EditorId.Length > 0)
        {
            // The caller forced the editor family - the "Open as text" gesture. The diagram
            // family is deliberately not consulted: this is the one way a diagram-routed file
            // opens as text at all, and the one way its diagram stream and its text stream can
            // coexist on one connection (modular-text-editors Requirements 5.2, 5.3).
            if (!TryResolveTextFile(request.ProjectId, request.Path, context, out var forcedRoot, out var forcedPath))
            {
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "The file cannot be opened as text."));
            }

            var forcedId = request.EditorId == "*" ? ResolvedEditorIdOf(forcedPath) : request.EditorId;
            var forcedFactory = _editorSessionFactories.Find(forcedId)
                ?? throw new RpcException(new Status(StatusCode.FailedPrecondition, $"No '{forcedId}' editor is deployed."));
            bodyPath = forcedPath;
            openedSession = new EditorSessionAdapter(forcedFactory.Open(watchId, forcedRoot, forcedPath), forcedId);
        }
        else if (TryResolveBody(request.ProjectId, request.Path, context, out var rootPath, out var diagramBody, out var origin, out var registrationPath))
        {
            var factory = _sessionFactories.Find(origin)
                ?? throw new RpcException(new Status(StatusCode.Unimplemented, $"'{origin}' diagrams cannot be opened yet."));
            bodyPath = diagramBody;
            openedSession = factory.Open(watchId, rootPath, bodyPath, registrationPath);
        }
        else if (TryResolveEditor(request.ProjectId, request.Path, context, out var editorRoot, out var fullPath, out var editorDefinitionId))
        {
            var editorFactory = _editorSessionFactories.Find(editorDefinitionId)
                ?? throw new RpcException(new Status(StatusCode.Unimplemented, $"The '{editorDefinitionId}' editor registered no session factory."));
            bodyPath = fullPath;
            openedSession = new EditorSessionAdapter(editorFactory.Open(watchId, editorRoot, fullPath), editorDefinitionId);
        }
        else
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "The diagram cannot be opened."));
        }

        await using var session = openedSession;
        var channel = Channel.CreateUnbounded<Delta>();

        void OnChanged(object? sender, DiagramDeltasEventArgs args)
        {
            foreach (var delta in args.Deltas)
            {
                channel.Writer.TryWrite(ToProto(delta));
            }
        }

        session.Changed += OnChanged;

        // Editor sessions stay out of the viewport registry: a text file has no viewport or
        // element moves to correlate, and the registry is keyed by (watch, path) - a text
        // stream of a file whose diagram stream is open on the same connection (Requirement
        // 5.3's both-at-once) would otherwise overwrite the diagram's registration and
        // deregister it again on close.
        var registersViewport = openedSession is not EditorSessionAdapter;
        if (registersViewport)
        {
            _viewports.Register(watchId, bodyPath, session, viewport => Apply(session, viewport, channel));
        }

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
            if (registersViewport)
            {
                _viewports.Remove(watchId, bodyPath);
            }

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

    /// <summary>
    /// The editor half of resolution, consulted only after the router said NotADiagram: the
    /// same root/containment/existence checks, then <see cref="EditorResolver"/>. An
    /// ambiguous claim refuses here - the conflict was already reported at startup, and this
    /// file names it again rather than opening in an arbitrary rival.
    /// </summary>
    private bool TryResolveEditor(
        Contracts.ShortGuid projectId,
        Path path,
        ServerCallContext context,
        out string rootPath,
        out string fullPath,
        out string editorId)
    {
        editorId = "";
        if (!TryResolveTextFile(projectId, path, context, out rootPath, out fullPath))
        {
            return false;
        }

        if (_editorResolver.Resolve(fullPath) is not EditorRouted routed)
        {
            fullPath = "";
            return false;
        }

        editorId = routed.Definition.Id;
        return true;
    }

    /// <summary>
    /// The shared first half of every editor-family resolution: the project root, the
    /// containment check, and the file's existence - nothing about which editor.
    /// </summary>
    private bool TryResolveTextFile(
        Contracts.ShortGuid projectId,
        Path path,
        ServerCallContext context,
        out string rootPath,
        out string fullPath)
    {
        fullPath = "";

        var userId = SessionContext.GetUserId(context);
        if (!ProjectRootResolver.TryResolve(_projectStore, userId, projectId, out rootPath, out _))
        {
            return false;
        }

        var combined = System.IO.Path.Combine([rootPath, .. path.Segments]);
        var full = System.IO.Path.GetFullPath(combined);
        if (!IsInside(rootPath, full) || !File.Exists(full))
        {
            return false;
        }

        fullPath = full;
        return true;
    }

    /// <summary>
    /// The definition id behind a forced <c>"*"</c>: whatever the resolver answers - the one
    /// claimant, the declared default of a legitimately shared extension, or the fallback.
    /// </summary>
    private string ResolvedEditorIdOf(string fullPath) =>
        _editorResolver.Resolve(fullPath) is EditorRouted routed
            ? routed.Definition.Id
            : throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"No editor can open '{System.IO.Path.GetFileName(fullPath)}': rival editors claim it and none is the default."));

    public override async Task<SaveTextResponse> SaveText(SaveTextRequest request, ServerCallContext context)
    {
        if (!TryResolveTextFile(request.ProjectId, request.Path, context, out var rootPath, out var fullPath))
        {
            return new SaveTextResponse { Error = "The file no longer exists." };
        }

        // Through the project's history, not straight to disk: a save is one undo away like
        // every other change (Requirement 6.2). Every open session of the file - text and
        // diagram alike - holds its own watcher, so the write comes back to all of them as an
        // ordinary pushed change; the file on disk is the tie-breaker by construction
        // (Requirements 5.3, 5.5).
        var result = await _historyStacks.Get(rootPath).ExecuteAsync(
            new SaveTextFileCommand(fullPath, request.Content), context.CancellationToken);

        _logger.Information("Saved {FullPath} through the history: {Outcome}", fullPath, result.IsSuccess ? "ok" : result.Error);
        return new SaveTextResponse { Error = result.IsSuccess ? "" : result.Error };
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
