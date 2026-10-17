using System.Threading.Channels;
using EtAlii.Adp.Authentication;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Projects;
using Grpc.Core;
using Path = EtAlii.Adp.Documents.Wire.Path;
namespace EtAlii.Adp.Workspace;

public sealed partial class DiagramService
{
    public override async Task Open(OpenDiagramRequest request, IServerStreamWriter<Delta> responseStream, ServerCallContext context)
    {
        var opened = OpenSession(SessionContext.GetUserId(context), request);
        await PumpAsync(opened, responseStream.WriteAsync, context.CancellationToken);
    }

    /// <summary>
    /// A diagram session that <see cref="OpenSession"/> resolved and opened, not yet pumping: the
    /// half of an open that can refuse, separated from the half that runs for the stream's life,
    /// so a caller can answer the refusal before it commits to a stream.
    /// </summary>
    public sealed record OpenedDiagram(IDiagramSession Session, string BodyPath, ShortGuid WatchId);

    /// <summary>
    /// Resolves what <paramref name="request"/> names and opens its session, or refuses with an
    /// <see cref="RpcException"/> carrying a <see cref="PermanentRefusal"/> code. The caller owns the
    /// session from here: <see cref="PumpAsync"/> disposes it.
    /// </summary>
    public OpenedDiagram OpenSession(ShortGuid userId, OpenDiagramRequest request)
    {
        var watchId = (ShortGuid)request.WatchId;

        // Which tool opens the file is ToolRouter's answer, the one place the precedence between
        // the kinds is decided: diagrams, then designers, then editors (code-structure FR-058).
        // The fallback editor answers for whatever remains, so a file that resolves to nothing at
        // all is a broken deployment, not an ordinary miss (modular-text-editors Requirement 3.2).
        IDiagramSession openedSession;
        string bodyPath;
        if (request.EditorId.Length > 0)
        {
            // The caller forced the editor family - the "Open as text" gesture. The diagram
            // family is deliberately not consulted: this is the one way a diagram-routed file
            // opens as text at all, and the one way its diagram stream and its text stream can
            // coexist on one connection (modular-text-editors Requirements 5.2, 5.3).
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
            bodyPath = forcedPath;
            openedSession = new EditorSessionAdapter(forcedFactory.Open(watchId, forced.Root.FullPath, forcedPath), forcedId);
        }
        else
        {
            // One open reads projects.json once: the root, the containment check and the file's
            // existence are resolved here and handed to the router (code-structure FR-062).
            if (!TryResolveTextFile(request.ProjectId, request.Path, userId, out var file))
            {
                _logger.Warning("Refusing to open {Path} on watch {WatchId}: it does not resolve inside the project", string.Join('/', request.Path.Segments), watchId);
                throw new RpcException(new Status(PermanentRefusal.CannotOpen, "The diagram cannot be opened."));
            }

            // The modules and the router take the root as a path; it is normalized already.
            var rootPath = file.Root.FullPath;
            var fullPath = file.FullPath;

            switch (_toolRouter.Route(fullPath, rootPath))
            {
                case DiagramRouted { BodyPath: { } diagramBody } routed:
                {
                    var origin = routed.Definition.Origin;
                    var factory = _sessionFactories.Find(origin);
                    if (factory is null)
                    {
                        _logger.Warning("Refusing to open {BodyPath} on watch {WatchId}: no session factory is deployed for {Origin}", diagramBody, watchId, origin.Key);
                        throw new RpcException(new Status(PermanentRefusal.NotDeployed, $"'{origin}' diagrams cannot be opened yet."));
                    }

                    _logger.Debug("Routed {FullPath} to {Origin}, body {BodyPath}, on watch {WatchId}", string.Join('/', request.Path.Segments), origin.Key, diagramBody, watchId);
                    bodyPath = diagramBody;
                    openedSession = factory.Open(watchId, rootPath, bodyPath, routed.RegistrationPath);

                    // From here on, an external write to this body or its .adp reaches the module's
                    // store as a Reload and every open session as pushed deltas (Requirement 5.3).
                    _reloadBridge.Track(file.Root, bodyPath, routed.RegistrationPath, origin);
                    break;
                }

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
                {
                    var editorId = editor.Definition.Id;
                    var editorFactory = _editorSessionFactories.Find(editorId);
                    if (editorFactory is null)
                    {
                        _logger.Warning("Refusing to open {FullPath} on watch {WatchId}: the {EditorId} editor registered no session factory", fullPath, watchId, editorId);
                        throw new RpcException(new Status(PermanentRefusal.NotDeployed, $"The '{editorId}' editor registered no session factory."));
                    }

                    _logger.Debug("Opening {FullPath} in the {EditorId} editor on watch {WatchId}", fullPath, editorId, watchId);
                    bodyPath = fullPath;
                    openedSession = new EditorSessionAdapter(editorFactory.Open(watchId, rootPath, fullPath), editorId);
                    break;
                }

                default:
                    // The refusal every misrouted open lands on: without this line there is nothing
                    // anywhere saying which file was asked for, and people debug it blind.
                    _logger.Warning("Refusing to open {Path} on watch {WatchId}: neither a diagram type nor an editor claims it", string.Join('/', request.Path.Segments), watchId);
                    throw new RpcException(new Status(PermanentRefusal.CannotOpen, "The diagram cannot be opened."));
            }
        }

        return new OpenedDiagram(openedSession, bodyPath, watchId);
    }

    /// <summary>
    /// Writes an opened session's baseline, then every later change, to <paramref name="write"/>
    /// until <paramref name="cancellationToken"/> ends it - to its own <c>Open</c> call, or to the
    /// tab's one <c>WorkspaceService.Watch</c> stream (two-tab-connection-wedge Requirement 3.1).
    /// Disposes the session when it returns.
    /// </summary>
    public async Task PumpAsync(OpenedDiagram opened, Func<Delta, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        (IDiagramSession openedSession, string bodyPath, ShortGuid watchId) = opened;
        await using var session = openedSession;
        var channel = Channel.CreateUnbounded<Delta>();

        session.Changed += OnChanged;

        // Editor sessions stay out of the viewport registry: a text file has no viewport or
        // element moves to correlate, and the registry is keyed by (watch, path) - a text
        // stream of a file whose diagram stream is open on the same connection (Requirement
        // 5.3's both-at-once) would otherwise overwrite the diagram's registration and
        // deregister it again on close.
        var registersViewport = openedSession is not EditorSessionAdapter;
        if (registersViewport)
        {
            // ReSharper disable once AccessToDisposedClosure - Reason: the registry calls this viewport callback only while it is registered, and the finally below deregisters it with _viewports.Remove before the await using disposes `session` at the end of PumpAsync.
            _viewports.Register(watchId, bodyPath, session, viewport => Apply(session, viewport, channel));
        }

        _logger.Information("Opened {BodyPath} on watch {WatchId}", bodyPath, watchId);

        try
        {
            var baseline = BaselineOf(session, bodyPath, watchId);
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

            await foreach (var delta in channel.Reader.ReadAllAsync(cancellationToken))
            {
                await write(delta, cancellationToken);
            }
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
            foreach (var delta in args.Deltas)
            {
                channel.Writer.TryWrite(DiagramWire.ToProto(delta));
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
    private static IReadOnlyList<DiagramDelta> BaselineOf(IDiagramSession session, string bodyPath, ShortGuid watchId)
    {
        try
        {
            return session.Baseline();
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

    private bool TryResolveBody(
        Documents.Wire.ShortGuid projectId,
        Path path,
        ShortGuid userId,
        out string bodyPath,
        out ToolOrigin origin)
    {
        bodyPath = "";
        origin = null!;

        // The client names the .adp file by project-relative path; the backend combines it
        // with the resolved root and refuses anything that escapes the root, so a path from
        // outside the project resolves to nothing (tech.md: no filesystem path crosses the
        // wire, and a client never reaches outside its workspace).
        if (!TryResolveTextFile(projectId, path, userId, out var file))
        {
            return false;
        }

        // The root is passed so a registration naming a shared body resolves to it, and so a
        // body: header pointing outside the project is refused (c4-diagrams Requirement 2.4).
        // BodyPath is null only when the router had no root to resolve a body: header against,
        // and one was passed above - so this refuses a document that genuinely does not resolve
        // rather than papering over a missing argument.
        if (_router.Route(file.FullPath, file.Root.FullPath) is not DiagramRouted { BodyPath: { } resolvedBody } routed)
        {
            return false;
        }

        bodyPath = resolvedBody;
        origin = routed.Definition.Origin;
        return true;
    }
}
