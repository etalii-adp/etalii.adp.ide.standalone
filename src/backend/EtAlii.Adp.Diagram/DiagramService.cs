using System.Threading.Channels;
using EtAlii.Adp.Authentication;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.Projects;
using Google.Protobuf;
using Grpc.Core;
using Serilog;
using Path = EtAlii.Adp.Documents.Wire.Path;
namespace EtAlii.Adp.Diagram;

/// <summary>
/// The one entry point for viewing and editing a diagram, whatever its type. It resolves the
/// project, resolves the diagram's type from its <c>.adp</c> file, opens the matching module's
/// <see cref="IDiagramSession"/>, and pumps that session's deltas onto the stream - mapping
/// the module's backend delta records to the contract's proto through <see cref="DiagramWire"/>. It knows no diagram type (mindmap-diagram Requirement 13.4).
/// </summary>
public sealed partial class DiagramService : Wire.DiagramService.DiagramServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<DiagramService>();

    private readonly IProjectStore _projectStore;
    private readonly DiagramFileRouter _router;
    private readonly DiagramSessionFactories _sessionFactories;
    private readonly EditorResolver _editorResolver;
    private readonly EditorSessionFactories _editorSessionFactories;
    private readonly IHistoryStackStore _historyStacks;
    private readonly DiagramDocumentReloadBridge _reloadBridge;
    private readonly IReadOnlyList<IDiagramToolboxProvider> _toolboxProviders;

    public DiagramService(
        IProjectStore projectStore,
        DiagramFileRouter router,
        DiagramSessionFactories sessionFactories,
        EditorResolver editorResolver,
        EditorSessionFactories editorSessionFactories,
        IDiagramViewportRegistry viewports,
        IHistoryStackStore historyStacks,
        DiagramDocumentReloadBridge reloadBridge,
        IEnumerable<IDiagramToolboxProvider> toolboxProviders)
    {
        _historyStacks = historyStacks;
        _projectStore = projectStore;
        _router = router;
        _sessionFactories = sessionFactories;
        _editorResolver = editorResolver;
        _editorSessionFactories = editorSessionFactories;
        _viewports = viewports;
        _reloadBridge = reloadBridge;
        _toolboxProviders = [.. toolboxProviders];
    }

    public override async Task<MoveElementResponse> MoveElement(MoveElementRequest request, ServerCallContext context)
    {
        var watchId = (ShortGuid)request.WatchId;
        if (!TryResolveBody(request.ProjectId, request.Path, context, out _, out var bodyPath, out _, out _))
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
        if (!TryResolveBody(request.ProjectId, request.Path, context, out _, out _, out var origin, out _))
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

    private static void Apply(IDiagramSession session, DiagramViewport viewport, Channel<Delta> channel)
    {
        var deltas = session.UpdateView(viewport);
        // What the new window cost: a module that streams by viewport answers with the elements
        // that just came into view (and the ones that left), and this is where that shows.
        // Silent at zero - a view update that changes nothing is the common case and would
        // otherwise drown the log during a pan.
        if (deltas.Count > 0)
        {
            _logger.Debug("The view update produced {DeltaCount} deltas: {DeltaKinds}", deltas.Count, KindsOf(deltas));
        }

        foreach (var delta in deltas)
        {
            channel.Writer.TryWrite(DiagramWire.ToProto(delta));
        }
    }

    /// <summary>
    /// A batch of deltas as "kind×count" pairs - what was sent, without the payloads. Element
    /// contents are deliberately not logged: a baseline can carry a whole document.
    /// </summary>
    private static string KindsOf(IReadOnlyList<DiagramDelta> deltas) =>
        string.Join(", ", deltas
            .GroupBy(delta => delta switch
            {
                DiagramAddDelta => "add",
                DiagramRemoveDelta => "remove",
                DiagramGroupDelta => "group",
                DiagramUngroupDelta => "ungroup",
                _ => delta.GetType().Name,
            })
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}×{group.Count()}"));

    /// <summary>
    /// The editor half of resolution, consulted only after the router said NotADiagram: the
    /// same root/containment/existence checks, then <see cref="EditorResolver"/>. An
    /// ambiguous claim refuses here - the conflict was already reported at startup, and this
    /// file names it again rather than opening in an arbitrary rival.
    /// </summary>
    private bool TryResolveEditor(
        Documents.Wire.ShortGuid projectId,
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
        Documents.Wire.ShortGuid projectId,
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
                PermanentRefusal.CannotOpen,
                $"No editor can open '{System.IO.Path.GetFileName(fullPath)}': rival editors claim it and none is the default."));

    public override async Task<SaveTextResponse> SaveText(SaveTextRequest request, ServerCallContext context)
    {
        if (!TryResolveTextFile(request.ProjectId, request.Path, context, out var rootPath, out var fullPath))
        {
            // The content is deliberately not logged - only which file the save asked for.
            _logger.Warning("Refused to save {Path}: it does not resolve inside the project any more", string.Join('/', request.Path.Segments));
            return new SaveTextResponse { Error = "The file no longer exists." };
        }

        // Through the project's history, not straight to disk: a save is one undo away like
        // every other change (Requirement 6.2). The write comes back to every open session of
        // the file as an ordinary pushed change - each text session through its own watcher,
        // each diagram through the reload bridge and its store - so the file on disk is the
        // tie-breaker by construction (Requirements 5.3, 5.5).
        var result = await _historyStacks.Get(rootPath).ExecuteAsync(
            new SaveTextFileCommand(fullPath, request.Content), context.CancellationToken);

        _logger.Information("Saved {FullPath} through the history: {Outcome}", fullPath, result.IsSuccess ? "ok" : result.Error);
        return new SaveTextResponse { Error = result.IsSuccess ? "" : result.Error };
    }
}
