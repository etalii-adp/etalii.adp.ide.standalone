using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Sessions;
using Grpc.Core;
using Path = EtAlii.Adp.Documents.Wire.Path;
namespace EtAlii.Adp.Workspace;

public sealed partial class DiagramService
{
    public override async Task Open(OpenDiagramRequest request, IServerStreamWriter<Delta> responseStream, ServerCallContext context)
    {
        var opened = _pump.Open(SessionContext.GetUserId(context), request);
        await _pump.RunAsync(opened, responseStream.WriteAsync, context.CancellationToken);
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
