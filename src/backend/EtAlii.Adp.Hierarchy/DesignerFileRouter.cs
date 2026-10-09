using EtAlii.Adp.Designer;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Decides which designer type a file on disk belongs to. A designer's document is always two
/// files - a registration naming the type on its first line, and the body beside it - so the
/// only thing that makes a file a designer's is a registration (knowledge-designer
/// Requirements 2.1 and 10.2). A body is never claimed by its extension: a designer stores its
/// documents in general formats, and a project is full of <c>.yaml</c> and <c>.json</c> files
/// that are nobody's table.
/// </summary>
/// <remarks>
/// Asked after <see cref="DiagramFileRouter"/> and before the editor family. It never reports
/// an unknown type: a registration whose first line it does not know may be a diagram's, or
/// name a module that is not deployed, and saying which is the diagram router's answer.
/// </remarks>
public sealed class DesignerFileRouter
{
    private static readonly ILogger _logger = Log.ForContext<DesignerFileRouter>();

    private readonly IDesignerDefinitionCatalog _catalog;

    public DesignerFileRouter(IDesignerDefinitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <param name="path">A registration file, or a file that may be a registration's body.</param>
    /// <param name="projectRoot">
    /// The project folder, when the caller knows it. Required to follow a registration's
    /// <c>body:</c> header, which is refused if it leaves the root. Callers that only want to
    /// know whether a file is a designer's may omit it.
    /// </param>
    public DesignerRouting Route(string path, string? projectRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Checked before any file is read: a host without a designer module pays nothing here.
        if (_catalog.All.Count == 0)
        {
            return new NotADesigner();
        }

        if (DiagramFilePair.IsRegistrationFile(path))
        {
            return RouteRegistration(path, projectRoot);
        }

        // A body is a designer's only through the registration of its own name beside it, and
        // only when that registration resolves back to this very file.
        var registration = IoPath.ChangeExtension(path, DiagramFileName.Extension);
        if (IoPath.GetExtension(path).Length > 0 &&
            File.Exists(registration) &&
            RouteRegistration(registration, projectRoot) is DesignerRouted { BodyPath: { } body } routed &&
            string.Equals(IoPath.GetFullPath(body), IoPath.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            return routed;
        }

        return new NotADesigner();
    }

    private DesignerRouting RouteRegistration(string adpPath, string? projectRoot)
    {
        var origin = DiagramFilePair.ReadMimeType(adpPath);
        var definition = origin is null
            ? null
            : _catalog.All.FirstOrDefault(candidate => string.Equals(candidate.Origin, origin, StringComparison.Ordinal));
        if (definition is null)
        {
            return new NotADesigner();
        }

        (string? named, _) = DiagramFilePair.ReadBodyAndViewHeaders(adpPath);
        if (named is null)
        {
            return new DesignerRouted(definition, adpPath, SiblingOf(adpPath, definition));
        }

        if (projectRoot is null && string.Equals(IoPath.GetFileName(named), named, StringComparison.Ordinal))
        {
            // A bare file name is beside the registration whatever the root is, so it needs none.
            return new DesignerRouted(definition, adpPath, IoPath.Combine(IoPath.GetDirectoryName(adpPath) ?? "", named));
        }

        if (projectRoot is null)
        {
            // The type is known; the body needs a root to resolve against, which the caller did not give.
            return new DesignerRouted(definition, adpPath, BodyPath: null);
        }

        var resolved = DiagramFilePair.ResolveWithin(projectRoot, IoPath.GetDirectoryName(adpPath) ?? projectRoot, named);
        if (resolved is null)
        {
            _logger.Warning("Not routing {Path}: its body: header names a file outside the project", adpPath);
            return new DesignerUnreadable(adpPath, "Its body: header names a file outside the project, which is refused rather than followed.");
        }

        return new DesignerRouted(definition, adpPath, resolved);
    }

    /// <summary>
    /// The body beside a registration that names none: the file of the registration's own name
    /// in the first of the type's formats that exists, or null when none does.
    /// </summary>
    private static string? SiblingOf(string adpPath, DesignerDefinition definition) =>
        definition.Formats
            .Select(format => IoPath.ChangeExtension(adpPath, format.Extension))
            .FirstOrDefault(File.Exists);
}
