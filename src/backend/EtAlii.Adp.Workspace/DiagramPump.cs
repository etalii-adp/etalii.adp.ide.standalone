using System.Threading.Channels;
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
/// A diagram held open on a connection: the file a request names resolved to the tool that opens
/// it and that tool's session opened, then the session's baseline and every later change written to
/// whatever carries them. Run by <see cref="DiagramService.Open"/> on a call of its own and by the
/// tab's one <see cref="WorkspaceService.Watch"/> stream alike (two-tab-connection-wedge
/// Requirement 3.1), so neither gRPC service calls the other (code-structure FR-067).
/// </summary>
public sealed class DiagramPump
{
    private static readonly ILogger _logger = Log.ForContext<DiagramPump>();

    private readonly IProjectStore _projectStore;
    private readonly ToolRouter _toolRouter;
    private readonly DiagramSessionFactories _sessionFactories;
    private readonly EditorResolver _editorResolver;
    private readonly EditorSessionFactories _editorSessionFactories;
    private readonly IDiagramViewportRegistry _viewports;
    private readonly DiagramDocumentReloadBridge _reloadBridge;

    public DiagramPump(
        IProjectStore projectStore,
        ToolRouter toolRouter,
        DiagramSessionFactories sessionFactories,
        EditorResolver editorResolver,
        EditorSessionFactories editorSessionFactories,
        IDiagramViewportRegistry viewports,
        DiagramDocumentReloadBridge reloadBridge)
    {
        _projectStore = projectStore;
        _toolRouter = toolRouter;
        _sessionFactories = sessionFactories;
        _editorResolver = editorResolver;
        _editorSessionFactories = editorSessionFactories;
        _viewports = viewports;
        _reloadBridge = reloadBridge;
    }

    /// <summary>
    /// Resolves what <paramref name="request"/> names and opens its session, or refuses with an
    /// <see cref="RpcException"/> carrying a <see cref="PermanentRefusal"/> code. The caller owns the
    /// session from here: <see cref="RunAsync"/> disposes it.
    /// </summary>
    public OpenedDiagram Open(ShortGuid userId, OpenDiagramRequest request)
    {
        var watchId = (ShortGuid)request.WatchId;

        // Which tool opens the file is ToolRouter's answer, the one place the precedence between
        // the kinds is decided: diagrams, then designers, then editors (code-structure FR-058).
        // The fallback editor answers for whatever remains, so a file that resolves to nothing at
        // all is a broken deployment, not an ordinary miss (modular-text-editors Requirement 3.2).
        return request.EditorId.Length > 0
            ? OpenAsText(userId, request, watchId)
            : OpenRouted(userId, request, watchId);
    }

    /// <summary>
    /// Opens the file in the editor the caller forced - the "Open as text" gesture. The diagram
    /// family is deliberately not consulted: this is the one way a diagram-routed file opens as
    /// text at all, and the one way its diagram stream and its text stream can coexist on one
    /// connection (modular-text-editors Requirements 5.2, 5.3).
    /// </summary>
    private OpenedDiagram OpenAsText(ShortGuid userId, OpenDiagramRequest request, ShortGuid watchId)
    {
        if (!TryResolveTextFile(request.ProjectId, request.Path, userId, out var forced))
        {
            // The client is told only that it failed; the log keeps what it asked for.
            _logger.Warning("Refusing to open {Path} as text on watch {WatchId}: it does not resolve inside the project", string.Join('/', request.Path.Segments), watchId);
            throw new RpcException(new Status(PermanentRefusal.CannotOpen, "The file cannot be opened as text."));
        }

        var forcedPath = forced.FullPath;
        var forcedId = request.EditorId == "*" ? ResolvedEditorIdOf(forcedPath) : request.EditorId;
        var forcedFactory = _editorSessionFactories.Find(forcedId);
        if (forcedFactory is null)
        {
            _logger.Warning("Refusing to open {FullPath} on watch {WatchId}: no {EditorId} editor is deployed", forcedPath, watchId, forcedId);
            throw new RpcException(new Status(PermanentRefusal.CannotOpen, $"No '{forcedId}' editor is deployed."));
        }

        _logger.Debug("Opening {FullPath} as text in the {EditorId} editor (forced) on watch {WatchId}", forcedPath, forcedId, watchId);
        return new OpenedDiagram(new EditorSessionAdapter(forcedFactory.Open(watchId, forced), forcedId), forcedPath, watchId);
    }

    /// <summary>Opens the file in the tool <see cref="ToolRouter"/> names for it, or refuses.</summary>
    private OpenedDiagram OpenRouted(ShortGuid userId, OpenDiagramRequest request, ShortGuid watchId)
    {
        // One open reads projects.json once: the root, the containment check and the file's
        // existence are resolved here and handed to the router (code-structure FR-062).
        if (!TryResolveTextFile(request.ProjectId, request.Path, userId, out var file))
        {
            _logger.Warning("Refusing to open {Path} on watch {WatchId}: it does not resolve inside the project", string.Join('/', request.Path.Segments), watchId);
            throw new RpcException(new Status(PermanentRefusal.CannotOpen, "The diagram cannot be opened."));
        }

        // The router takes the root as a path; it is normalized already.
        switch (_toolRouter.Route(file.FullPath, file.Root.FullPath))
        {
            case DiagramRouted { BodyPath: { } diagramBody } routed:
                return OpenDiagram(file, routed, diagramBody, request, watchId);

            case DesignerRouted designer:
                // A designer's registration and its body are that designer's to open, and must
                // not fall through to a text editor that would present a table as its raw file
                // (knowledge-designer Requirement 10.2). The designer family has no session on
                // this stream yet, so the answer is a refusal that names the type rather than a
                // text stream of the wrong thing.
                _logger.Warning("Refusing to open {Path} on watch {WatchId}: it is a {Origin} document, and designers cannot be opened on this stream", string.Join('/', request.Path.Segments), watchId, designer.Definition.Origin);
                throw new RpcException(new Status(PermanentRefusal.NotDeployed, $"'{designer.Definition.Origin}' documents cannot be opened yet."));

            case ToolUnreadable unreadable:
                // A registration that names its tool but cannot be served is refused with the
                // router's reason, as the problems panel reports it, rather than shown as a text
                // file of its own; "Open as text" still opens it for repair.
                _logger.Warning("Refusing to open {Path} on watch {WatchId}: {Reason}", string.Join('/', request.Path.Segments), watchId, unreadable.Reason);
                throw new RpcException(new Status(PermanentRefusal.CannotOpen, $"'{System.IO.Path.GetFileName(unreadable.Path)}' cannot be opened. {unreadable.Reason}"));

            case EditorRouted editor:
                return OpenInEditor(file, editor, watchId);

            default:
                // The refusal every misrouted open lands on: without this line there is nothing
                // anywhere saying which file was asked for, and people debug it blind.
                _logger.Warning("Refusing to open {Path} on watch {WatchId}: neither a diagram type nor an editor claims it", string.Join('/', request.Path.Segments), watchId);
                throw new RpcException(new Status(PermanentRefusal.CannotOpen, "The diagram cannot be opened."));
        }
    }

    private OpenedDiagram OpenDiagram(ProjectPath file, DiagramRouted routed, string bodyPath, OpenDiagramRequest request, ShortGuid watchId)
    {
        var origin = routed.Definition.Origin;
        var factory = _sessionFactories.Find(origin);
        if (factory is null)
        {
            _logger.Warning("Refusing to open {BodyPath} on watch {WatchId}: no session factory is deployed for {Origin}", bodyPath, watchId, origin.Key);
            throw new RpcException(new Status(PermanentRefusal.NotDeployed, $"'{origin}' diagrams cannot be opened yet."));
        }

        _logger.Debug("Routed {FullPath} to {Origin}, body {BodyPath}, on watch {WatchId}", string.Join('/', request.Path.Segments), origin.Key, bodyPath, watchId);
        var session = factory.Open(watchId, file.Root, bodyPath, routed.RegistrationPath);

        // From here on, an external write to this body or its .adp reaches the module's
        // store as a Reload and every open session as pushed deltas (Requirement 5.3).
        _reloadBridge.Track(file.Root, bodyPath, routed.RegistrationPath, origin);
        return new OpenedDiagram(session, bodyPath, watchId);
    }

    private OpenedDiagram OpenInEditor(ProjectPath file, EditorRouted editor, ShortGuid watchId)
    {
        var fullPath = file.FullPath;
        var editorId = editor.Definition.Id;
        var editorFactory = _editorSessionFactories.Find(editorId);
        if (editorFactory is null)
        {
            _logger.Warning("Refusing to open {FullPath} on watch {WatchId}: the {EditorId} editor registered no session factory", fullPath, watchId, editorId);
            throw new RpcException(new Status(PermanentRefusal.NotDeployed, $"The '{editorId}' editor registered no session factory."));
        }

        _logger.Debug("Opening {FullPath} in the {EditorId} editor on watch {WatchId}", fullPath, editorId, watchId);
        return new OpenedDiagram(new EditorSessionAdapter(editorFactory.Open(watchId, file), editorId), fullPath, watchId);
    }

    /// <summary>
    /// Writes an opened session's baseline, then every later change, to <paramref name="write"/>
    /// until <paramref name="cancellationToken"/> ends it - to its own <c>Open</c> call, or to the
    /// tab's one <c>WorkspaceService.Watch</c> stream (two-tab-connection-wedge Requirement 3.1).
    /// Disposes the session when it returns.
    /// </summary>
    public async Task RunAsync(OpenedDiagram opened, Func<Delta, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        (IDiagramSession openedSession, string bodyPath, ShortGuid watchId) = opened;
        await using var session = openedSession;
        // Bounded by batches - what one change or one view update produced - rather than by deltas,
        // so a batch larger than the bound, such as a zoom out over a large diagram, never ends the
        // stream it would only produce again once re-opened. A tab that stops reading ends it (B-28).
        var channel = StreamBound.Ending<Delta[]>($"Diagram stream of {bodyPath} on watch {watchId}");

        session.Changed += OnChanged;

        // Editor sessions stay out of the viewport registry: a text file has no viewport or
        // element moves to correlate, and the registry is keyed by (watch, path) - a text
        // stream of a file whose diagram stream is open on the same connection (Requirement
        // 5.3's both-at-once) would otherwise overwrite the diagram's registration and
        // deregister it again on close.
        var registersViewport = openedSession is not EditorSessionAdapter;
        if (registersViewport)
        {
            // ReSharper disable once AccessToDisposedClosure - Reason: the registry calls this viewport callback only while it is registered, and the finally below deregisters it with _viewports.Remove before the await using disposes `session` at the end of RunAsync.
            _viewports.Register(watchId, bodyPath, session, viewport => Apply(session, viewport, channel));
        }

        _logger.Information("Opened {BodyPath} on watch {WatchId}", bodyPath, watchId);

        try
        {
            await WriteAsync(session, bodyPath, watchId, channel, write, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Debug("Diagram stream on watch {WatchId} closed by the client", watchId);
        }
        finally
        {
            session.Changed -= OnChanged;
            if (registersViewport)
            {
                _viewports.Remove(watchId, bodyPath, session);
            }

            _logger.Information("Closed {BodyPath} on watch {WatchId}", bodyPath, watchId);
        }

        return;

        void OnChanged(object? sender, DiagramDeltasEventArgs args)
        {
            // What the module decided to push, by kind - the one place a reader can see that an
            // edit produced (say) a remove plus an add rather than the move they expected.
            _logger.Debug(
                "Pushing {DeltaCount} deltas for {BodyPath} on watch {WatchId}: {DeltaKinds}",
                args.Deltas.Count,
                bodyPath,
                watchId,
                KindsOf(args.Deltas));

            // MUST STAY NON-BLOCKING: DiagramDocumentChangeHandler raises into this while holding its lock.
            Push(args.Deltas, channel);
        }
    }

    /// <summary>The session's baseline, then every batch of changes <paramref name="channel"/> carries, written as they come.</summary>
    private static async Task WriteAsync(
        IDiagramSession session,
        string bodyPath,
        ShortGuid watchId,
        Channel<Delta[]> channel,
        Func<Delta, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        var baseline = await BaselineOfAsync(session, bodyPath, watchId, cancellationToken);
        _logger.Debug(
            "Sending the baseline of {BodyPath} on watch {WatchId}: {DeltaCount} deltas, {DeltaKinds}",
            bodyPath,
            watchId,
            baseline.Count,
            KindsOf(baseline));

        // A diagram with nothing in it still has a baseline: an empty Add. The client knows
        // the open has answered only when the first delta arrives, so a session whose
        // baseline is no deltas at all - a Helm chart over a folder without Chart.yaml, an
        // empty mindmap - left its tab on "Opening…" until something changed. Said here,
        // once, rather than in every module's Baseline().
        if (baseline.Count == 0)
        {
            await write(new Delta { Add = new Add() }, cancellationToken);
        }

        foreach (var delta in baseline)
        {
            await write(DiagramWire.ToProto(delta), cancellationToken);
        }

        await foreach (var batch in channel.Reader.ReadAllAsync(cancellationToken))
        {
            foreach (var delta in batch)
            {
                await write(delta, cancellationToken);
            }
        }
    }

    /// <summary>
    /// The session's baseline, or a permanent refusal when it cannot be made. A module's
    /// <c>Baseline()</c> that throws - a mindmap with no root node, a body that does not parse -
    /// will throw again on every re-open, so it is refused with <see cref="PermanentRefusal.CannotOpen"/>
    /// and the module's own reason, and the client's tab says so instead of re-opening for ever.
    /// Said here, once, for every module.
    /// </summary>
    private static async Task<IReadOnlyList<DiagramDelta>> BaselineOfAsync(IDiagramSession session, string bodyPath, ShortGuid watchId, CancellationToken cancellationToken)
    {
        try
        {
            return await session.BaselineAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not RpcException)
        {
            _logger.Warning(exception, "Refusing {BodyPath} on watch {WatchId}: its baseline could not be made", bodyPath, watchId);
            // The file's name only: no filesystem path crosses the wire, so the folder is taken
            // out of the module's sentence too, where an I/O failure would otherwise carry it.
            var folder = System.IO.Path.GetDirectoryName(bodyPath) + System.IO.Path.DirectorySeparatorChar;
            var fileName = System.IO.Path.GetFileName(bodyPath);
            var reason = exception.Message.Replace(folder, "", StringComparison.Ordinal);
            // And a module that already named the file does not have it named twice.
            if (reason.StartsWith(fileName + ": ", StringComparison.Ordinal))
            {
                reason = reason[(fileName.Length + 2)..];
            }

            throw new RpcException(new Status(PermanentRefusal.CannotOpen, $"'{fileName}' could not be read: {reason}"));
        }
    }

    private static void Apply(IDiagramSession session, DiagramViewport viewport, Channel<Delta[]> channel)
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

        Push(deltas, channel);
    }

    private static void Push(IReadOnlyList<DiagramDelta> deltas, Channel<Delta[]> channel)
    {
        if (deltas.Count > 0)
        {
            channel.Writer.TryWrite([.. deltas.Select(DiagramWire.ToProto)]);
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
    /// The shared first half of every open: the project root, the containment check, and the
    /// file's existence - nothing about which tool.
    /// </summary>
    private bool TryResolveTextFile(
        Documents.Wire.ShortGuid projectId,
        Path path,
        ShortGuid userId,
        out ProjectPath file) =>
        ProjectTextFile.TryResolve(_projectStore, projectId, path, userId, out file);

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
}
