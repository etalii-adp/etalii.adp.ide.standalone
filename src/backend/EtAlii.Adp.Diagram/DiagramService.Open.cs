using System.Threading.Channels;
using EtAlii.Adp.Authentication;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Projects;
using Grpc.Core;
using Path = EtAlii.Adp.Common.Wire.Path;
namespace EtAlii.Adp.Diagram;

public sealed partial class DiagramService
{
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
                // The client is told only that it failed; the log keeps what it asked for.
                _logger.Warning("Refusing to open {Path} as text on watch {WatchId}: it does not resolve inside the project", string.Join('/', request.Path.Segments), watchId);
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "The file cannot be opened as text."));
            }

            var forcedId = request.EditorId == "*" ? ResolvedEditorIdOf(forcedPath) : request.EditorId;
            var forcedFactory = _editorSessionFactories.Find(forcedId);
            if (forcedFactory is null)
            {
                _logger.Warning("Refusing to open {FullPath} on watch {WatchId}: no {EditorId} editor is deployed", forcedPath, watchId, forcedId);
                throw new RpcException(new Status(StatusCode.FailedPrecondition, $"No '{forcedId}' editor is deployed."));
            }

            _logger.Debug("Opening {FullPath} as text in the {EditorId} editor (forced) on watch {WatchId}", forcedPath, forcedId, watchId);
            bodyPath = forcedPath;
            openedSession = new EditorSessionAdapter(forcedFactory.Open(watchId, forcedRoot, forcedPath), forcedId);
        }
        else if (TryResolveBody(request.ProjectId, request.Path, context, out var rootPath, out var diagramBody, out var origin, out var registrationPath))
        {
            var factory = _sessionFactories.Find(origin);
            if (factory is null)
            {
                _logger.Warning("Refusing to open {BodyPath} on watch {WatchId}: no session factory is deployed for {Origin}", diagramBody, watchId, origin.Key);
                throw new RpcException(new Status(StatusCode.Unimplemented, $"'{origin}' diagrams cannot be opened yet."));
            }

            _logger.Debug("Routed {FullPath} to {Origin}, body {BodyPath}, on watch {WatchId}", string.Join('/', request.Path.Segments), origin.Key, diagramBody, watchId);
            bodyPath = diagramBody;
            openedSession = factory.Open(watchId, rootPath, bodyPath, registrationPath);

            // From here on, an external write to this body or its .adp reaches the module's
            // store as a Reload and every open session as pushed deltas (Requirement 5.3).
            _reloadBridge.Track(rootPath, bodyPath, registrationPath, origin);
        }
        else if (TryResolveEditor(request.ProjectId, request.Path, context, out var editorRoot, out var fullPath, out var editorDefinitionId))
        {
            var editorFactory = _editorSessionFactories.Find(editorDefinitionId);
            if (editorFactory is null)
            {
                _logger.Warning("Refusing to open {FullPath} on watch {WatchId}: the {EditorId} editor registered no session factory", fullPath, watchId, editorDefinitionId);
                throw new RpcException(new Status(StatusCode.Unimplemented, $"The '{editorDefinitionId}' editor registered no session factory."));
            }

            _logger.Debug("Opening {FullPath} in the {EditorId} editor on watch {WatchId}", fullPath, editorDefinitionId, watchId);
            bodyPath = fullPath;
            openedSession = new EditorSessionAdapter(editorFactory.Open(watchId, editorRoot, fullPath), editorDefinitionId);
        }
        else
        {
            // The refusal every misrouted open lands on: without this line there is nothing
            // anywhere saying which file was asked for, and people debug it blind.
            _logger.Warning("Refusing to open {Path} on watch {WatchId}: neither a diagram type nor an editor claims it", string.Join('/', request.Path.Segments), watchId);
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "The diagram cannot be opened."));
        }

        await using var session = openedSession;
        var channel = Channel.CreateUnbounded<Delta>();

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
            // ReSharper disable once AccessToDisposedClosure
            // Reason: probably a false negative. The _viewport.Remove is called in the finally below.
            _viewports.Register(watchId, bodyPath, session, viewport => Apply(session, viewport, channel));
        }

        _logger.Information("Opened {BodyPath} on watch {WatchId}", bodyPath, watchId);

        try
        {
            var baseline = session.Baseline();
            _logger.Debug(
                "Sending the baseline of {BodyPath} on watch {WatchId}: {DeltaCount} deltas, {DeltaKinds}",
                bodyPath,
                watchId,
                baseline.Count,
                KindsOf(baseline));

            foreach (var delta in baseline)
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

    private bool TryResolveBody(
        Common.Wire.ShortGuid projectId,
        Path path,
        ServerCallContext context,
        out string rootPath,
        out string bodyPath,
        out DiagramOrigin origin,
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
}
