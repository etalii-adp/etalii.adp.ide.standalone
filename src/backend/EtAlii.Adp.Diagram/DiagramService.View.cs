using EtAlii.Adp.Diagram.Wire;
using Grpc.Core;

namespace EtAlii.Adp.Diagram;

public sealed partial class DiagramService
{
    private readonly IDiagramViewportRegistry _viewports;

    public override Task<UpdateViewResponse> UpdateView(UpdateViewRequest request, ServerCallContext context)
    {
        var watchId = (ShortGuid)request.WatchId;
        if (!TryResolveBody(request.ProjectId, request.Path, context, out _, out var bodyPath, out _, out _))
        {
            // Debug, not Warning: view reports arrive continuously, and a stale one racing a
            // closed diagram is ordinary. The path is what a reader needs to correlate.
            _logger.Debug("Ignoring a view report for {Path} on watch {WatchId}: the diagram does not resolve", string.Join('/', request.Path.Segments), watchId);
            return Task.FromResult(new UpdateViewResponse { Error = "The diagram is not open." });
        }

        var box = request.View.BoundingBox;
        var viewport = new DiagramViewport(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y);
        // The window the client says it is looking at. What the module decides to send back for
        // it - if anything - is logged by Apply, so the pair reads as request and answer.
        _logger.Debug(
            "View reported for {BodyPath} on watch {WatchId}: ({MinX}, {MinY}) to ({MaxX}, {MaxY})",
            bodyPath,
            watchId,
            viewport.MinX,
            viewport.MinY,
            viewport.MaxX,
            viewport.MaxY);

        if (!_viewports.Report(watchId, bodyPath, viewport))
        {
            _logger.Debug("Ignoring a view report for {BodyPath}: watch {WatchId} has no open stream for it", bodyPath, watchId);
            return Task.FromResult(new UpdateViewResponse { Error = "The diagram is not open on this connection." });
        }

        return Task.FromResult(new UpdateViewResponse());
    }

    private static bool IsInside(string rootPath, string fullPath)
    {
        var root = System.IO.Path.GetFullPath(rootPath)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
