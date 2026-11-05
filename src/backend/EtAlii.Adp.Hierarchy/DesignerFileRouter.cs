using EtAlii.Adp.Designer;
using EtAlii.Adp.Diagram;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Decides which designer type a file on disk belongs to. A designer's document is a registration
/// naming the type on its first line and the body beside it (knowledge-designer Requirements 2.1
/// and 10.2), or a body whose own first line is the envelope of a type that claims it by that
/// envelope (<see cref="DesignerDefinition.Claim"/>, spec 029 R3). A body is never claimed by its
/// extension alone: a designer stores its documents in general formats, and a project is full of
/// <c>.yaml</c>, <c>.json</c> and <c>.md</c> files that are nobody's document.
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
    private readonly FirstLineCache _firstLines = new();

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

    /// <summary>
    /// The document a body is by its envelope (spec 029 R3, R4): the designer type whose <see cref="DesignerDefinition.Claim"/>
    /// takes the file by its first line, with the registration of that type that names it, or none. Null when no type
    /// claims it so. Only the first line of a file of a claimed extension is read, and that once until the file changes.
    /// </summary>
    /// <param name="path">A file that may be a body.</param>
    public DesignerRouted? RouteByEnvelope(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return ClaimantOf(path) is { } definition
            ? new DesignerRouted(definition, RegistrationOf(path, definition), path)
            : null;
    }

    /// <summary>The designer type whose envelope the file at <paramref name="path"/> carries on its first line, or null.</summary>
    public DesignerDefinition? ClaimantOf(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (DiagramFilePair.IsRegistrationFile(path))
        {
            return null;
        }

        var extension = IoPath.GetExtension(path);
        var claimants = _catalog.All
            .Where(definition => definition.Claim is { } claim && claim.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (claimants.Count == 0)
        {
            return null;
        }

        var head = _firstLines.Read(path);
        var name = IoPath.GetFileName(path);
        return head.Length == 0 ? null : claimants.FirstOrDefault(definition => definition.Claim!.Matches(name, head));
    }

    /// <summary>
    /// The registration of a body that has none in hand (FBL section 8.4): the <c>.adp</c> beside it whose first line is
    /// the type's origin and whose <c>body:</c> names the body, or that names none and has the body's base name.
    /// </summary>
    private static string? RegistrationOf(string bodyPath, DesignerDefinition definition)
    {
        var folder = IoPath.GetDirectoryName(bodyPath);
        if (folder is null || !Directory.Exists(folder))
        {
            return null;
        }

        var own = IoPath.ChangeExtension(bodyPath, DiagramFileName.Extension);
        var body = IoPath.GetFullPath(bodyPath);
        var candidates = Directory.EnumerateFiles(folder, "*" + DiagramFileName.Extension)
            .OrderBy(candidate => !string.Equals(candidate, own, StringComparison.OrdinalIgnoreCase))
            .ThenBy(candidate => candidate, StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var registration = AdpRegistration.Read(candidate);
            if (!string.Equals(registration?.MimeType, definition.Origin, StringComparison.Ordinal))
            {
                continue;
            }

            var names = registration!.Body is { } named
                ? string.Equals(IoPath.GetFullPath(IoPath.Combine(folder, named)), body, StringComparison.OrdinalIgnoreCase)
                : string.Equals(candidate, own, StringComparison.OrdinalIgnoreCase);
            if (names)
            {
                return candidate;
            }
        }

        return null;
    }

    private DesignerRouting RouteRegistration(string adpPath, string? projectRoot)
    {
        // Read once: the designer type and the body header come from this one registration.
        var registration = AdpRegistration.Read(adpPath);
        var origin = registration?.MimeType;
        var definition = origin is null
            ? null
            : _catalog.All.FirstOrDefault(candidate => string.Equals(candidate.Origin, origin, StringComparison.Ordinal));
        if (registration is null || definition is null)
        {
            return new NotADesigner();
        }

        var named = registration.Body;
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
