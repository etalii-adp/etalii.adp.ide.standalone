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

    /// <param name="path">The path to the diagram file.</param>
    /// <param name="projectRoot">
    /// The project folder, when the caller knows it. Required to follow a registration's
    /// <c>body:</c> header, which is project-relative and refused if it escapes the root
    /// (c4-diagrams Requirement 2.4). Callers that only want to know *whether* a file is a
    /// diagram may omit it: without a root the derived sibling is reported, which answers that
    /// question just as well.
    /// </param>
    public DiagramRouting Route(string path, string? projectRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (DiagramFilePair.IsRegistrationFile(path))
        {
            return RouteRegistration(path, projectRoot);
        }

        return RouteBody(path, projectRoot);
    }

    /// <summary>
    /// The extensions that types from **different vendors** claim - a deployment error,
    /// reported once at startup, because a body dropped in on its own cannot be routed.
    /// </summary>
    /// <remarks>
    /// Several types of one vendor sharing an extension is not an error but a design: the C4
    /// family is seven types over one document format and one engine, which is exactly what
    /// "model once, view many" means. Those are disambiguated by the document itself rather
    /// than by the file name, so they are not reported here (c4-diagrams Requirement 2.6).
    /// </remarks>
    /// <summary>
    /// Whether any discovered type's diagram is a <see cref="DiagramSubject.Folder"/> - the one
    /// question worth asking before walking a tree looking for an enclosing registration.
    /// </summary>
    /// <remarks>
    /// Deployments without such a type - which is every one before <c>ansible/structure</c>
    /// ships - answer false and pay no directory probes at all for it.
    /// </remarks>
    public bool HasFolderSubjectTypes => _catalog.All.Any(definition => definition.HasFolderSubject);

    public IReadOnlyList<string> AmbiguousExtensions() =>
        _catalog.All
            .Where(definition => definition.HasDocumentSibling)
            .GroupBy(definition => definition.Extension, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(definition => definition.Origin.Vendor).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => group.Key)
            .ToArray();

    /// <summary>
    /// Whether any discovered type declares <paramref name="path"/>'s extension, shared or
    /// not. Deliberately not <see cref="Route"/>: Route refuses a shared extension on sight,
    /// while a registrable file counts as potential (small-refinements Requirement 3.2) - a
    /// .yml an Azure Pipeline would happily register is claimed here even though Route will
    /// not route it unasked.
    /// </summary>
    public bool ClaimsExtensionOf(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var extension = IoPath.GetExtension(path);
        return extension.Length > 0
            && _catalog.All.Any(definition => definition.DeclaresExtension(extension));
    }

    private DiagramRouting RouteRegistration(string adpPath, string? projectRoot)
    {
        var mimeType = DiagramFilePair.ReadMimeType(adpPath);
        if (mimeType is null)
        {
            return new DiagramUnreadable(adpPath, "The registration file could not be read.");
        }

        var definition = _catalog.All.FirstOrDefault(candidate => string.Equals(candidate.Origin.MimeType, mimeType, StringComparison.Ordinal));
        if (definition is null)
        {
            // Named rather than swallowed: the user can tell a missing module from a typo.
            return new DiagramUnknownType(adpPath, mimeType);
        }

        if (!definition.HasDocumentSibling)
        {
            return new DiagramRouted(definition, adpPath, adpPath);
        }

        var body = DiagramFilePair.BodyOf(adpPath, _catalog, projectRoot);
        if (body is null)
        {
            // Only a body: header that escapes the project root gets here - the registration
            // read fine and its type is known. Refused rather than followed: the header is
            // user-editable text (Requirement 2.4).
            _logger.Warning("Not routing {Path}: its body: header names a document outside the project", adpPath);
            return new DiagramUnreadable(
                adpPath,
                "Its body: header names a document outside the project, which is refused rather than followed.");
        }

        // An unresolved header travels as null, not as the empty string BodyOf uses to mean
        // "you did not give me a root to resolve against". The type is still known, so callers
        // that only want that keep working; callers that want the document have to pass a root.
        return new DiagramRouted(definition, adpPath, body.Value.Path.Length == 0 ? null : body.Value.Path);
    }

    private DiagramRouting RouteBody(string bodyPath, string? projectRoot)
    {
        var extension = IoPath.GetExtension(bodyPath);
        if (extension.Length == 0)
        {
            return new NotADiagram(bodyPath);
        }

        // The registration file wins where it exists; this path is only for a body dropped
        // into the project on its own (Requirement 2.7).
        var registration = IoPath.ChangeExtension(bodyPath, DiagramFileName.Extension);
        if (File.Exists(registration))
        {
            return RouteRegistration(registration, projectRoot);
        }

        var claimants = _catalog.All
            .Where(definition => definition.DeclaresExtension(extension))
            .ToArray();

        // An extension every claimant calls shared is one nobody may claim on sight: a repository
        // is full of .yml files that are not pipelines, and routing one on its extension alone
        // would present all of them as diagrams. Such a file becomes a diagram when the user
        // registers it, which writes the .adp the branch above already honours
        // (azure-pipeline-diagram Requirement 2.2). Checked before the count switch so it applies
        // whether one type claims the extension or several.
        if (claimants.Length > 0 && claimants.All(definition => definition.SharedExtension))
        {
            _logger.Debug(
                "Not routing {Path}: {Extension} is shared, so it opens only through an .adp registration",
                bodyPath,
                extension);
            return new NotADiagram(bodyPath);
        }

        // A type that calls the extension shared never claims a bare body - that is what the
        // stance means - so it is dropped here rather than left in the running. Without this a
        // family whose alternative readings share the anchor's extension could win the bare file
        // on catalog order alone: with w3c/owl, w3c/rdf and w3c/skos all declaring .ttl, the
        // ontology reading sorts first and a plain data graph would open as an empty ontology.
        // The anchor - the one type that does claim the extension - is what a bare file wants.
        claimants = [.. claimants.Where(definition => !definition.SharedExtension)];

        switch (claimants.Length)
        {
            case 0:
                return new NotADiagram(bodyPath);

            case 1:
                return new DiagramRouted(claimants[0], RegistrationPath: null, bodyPath);

            default:
                var vendors = claimants.Select(definition => definition.Origin.Vendor).Distinct(StringComparer.Ordinal).ToArray();
                if (vendors.Length == 1)
                {
                    // One vendor's family sharing a document format: which of its types this
                    // document is depends on the view the document declares, which only that
                    // module can read. They all resolve to the same engine, so routing to the
                    // family's first type is enough - the module then opens the view the
                    // document actually declares (Requirement 2.6, and the fallback in the
                    // session factory).
                    _logger.Debug(
                        "Routing {Path} to the {Vendor} family: {Count} of its types share {Extension}, and the document names the view",
                        bodyPath,
                        vendors[0],
                        claimants.Length,
                        extension);
                    return new DiagramRouted(claimants[0], RegistrationPath: null, bodyPath);
                }

                // Guessing which module owns a body is worse than saying the deployment is
                // ambiguous, which mirrors how discovery refuses a duplicate origin.
                _logger.Warning(
                    "Not routing {Path}: {Extension} is claimed by {Claimants}",
                    bodyPath,
                    extension,
                    claimants.Select(definition => definition.Origin.Key));
                return new DiagramAmbiguousExtension(bodyPath, extension, claimants);
        }
    }
}
