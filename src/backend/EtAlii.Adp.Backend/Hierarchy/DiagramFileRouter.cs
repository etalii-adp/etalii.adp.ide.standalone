using EtAlii.Adp.Diagram;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// Decides which diagram type a file on disk belongs to. A registration file is routed by
/// its MIME-type first line; a body file with no registration beside it is routed by the
/// extension its type declares - and never by guessing when two types claim one extension
/// (mindmap-diagram Requirements 2.3, 2.6, 2.7, 2.8).
/// </summary>
public sealed class DiagramFileRouter
{
    private static readonly ILogger _logger = Log.ForContext<DiagramFileRouter>();

    private readonly IDiagramDefinitionCatalog _catalog;

    public DiagramFileRouter(IDiagramDefinitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    public DiagramRouting Route(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (DiagramFilePair.IsRegistrationFile(path))
        {
            return RouteRegistration(path);
        }

        return RouteBody(path);
    }

    /// <summary>The extensions more than one discovered type claims - a deployment error, reported once at startup.</summary>
    public IReadOnlyList<string> AmbiguousExtensions() =>
        _catalog.All
            .Where(definition => definition.HasDocumentSibling)
            .GroupBy(definition => definition.Extension, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

    private DiagramRouting RouteRegistration(string adpPath)
    {
        var mimeType = DiagramFilePair.ReadMimeType(adpPath);
        if (mimeType is null)
        {
            return new DiagramRouting.Unreadable(adpPath);
        }

        var definition = _catalog.All.FirstOrDefault(candidate => string.Equals(candidate.Origin.MimeType, mimeType, StringComparison.Ordinal));
        if (definition is null)
        {
            // Named rather than swallowed: the user can tell a missing module from a typo.
            return new DiagramRouting.UnknownType(adpPath, mimeType);
        }

        var body = definition.HasDocumentSibling ? DiagramFilePair.SiblingPathFor(adpPath, definition.Extension) : adpPath;
        return new DiagramRouting.Routed(definition, adpPath, body);
    }

    private DiagramRouting RouteBody(string bodyPath)
    {
        var extension = IoPath.GetExtension(bodyPath);
        if (extension.Length == 0)
        {
            return new DiagramRouting.NotADiagram(bodyPath);
        }

        // The registration file wins where it exists; this path is only for a body dropped
        // into the project on its own (Requirement 2.7).
        var registration = IoPath.ChangeExtension(bodyPath, DiagramFileName.Extension);
        if (File.Exists(registration))
        {
            return RouteRegistration(registration);
        }

        var claimants = _catalog.All
            .Where(definition => string.Equals(definition.Extension, extension, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        switch (claimants.Length)
        {
            case 0:
                return new DiagramRouting.NotADiagram(bodyPath);

            case 1:
                return new DiagramRouting.Routed(claimants[0], RegistrationPath: null, bodyPath);

            default:
                // Guessing which module owns a body is worse than saying the deployment is
                // ambiguous, which mirrors how discovery refuses a duplicate origin.
                _logger.Warning(
                    "Not routing {Path}: {Extension} is claimed by {Claimants}",
                    bodyPath,
                    extension,
                    claimants.Select(definition => definition.Origin.Key));
                return new DiagramRouting.Ambiguous(bodyPath, extension, claimants);
        }
    }
}
