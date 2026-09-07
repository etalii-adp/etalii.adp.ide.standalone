using System.Xml.Linq;
using Serilog;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Reads one project file for what the graph draws: its <c>ProjectReference</c>s, its
/// <c>PackageReference</c>s and the frameworks it targets.
/// </summary>
/// <remarks>
/// <para>
/// <b>Central package management is the normal case here, not an edge case.</b> This repository
/// sets <c>ManagePackageVersionsCentrally</c> and writes bare
/// <c>&lt;PackageReference Include="Serilog" /&gt;</c> lines whose version lives in a
/// <c>Directory.Packages.props</c> further up the tree. So a reference carrying no
/// <c>Version</c> is resolved by walking upward from the project's own folder, as MSBuild does,
/// taking the first <c>PackageVersion</c> that names the package.
/// </para>
/// <para>
/// <b>What this reader does not resolve is stated rather than silently dropped</b>
/// (Requirement 3.6, and the correctness rule that an edge is never invented and never
/// silently omitted). It reads the project file as written: it does not evaluate MSBuild
/// conditions, does not follow <c>Import</c> elements, and does not expand properties inside a
/// version or an include. A reference it cannot put a version to still becomes an edge, with
/// the version recorded as not discoverable - because dropping the edge would misrepresent the
/// project, while an edge with an unknown version is the truth. The module readme carries this
/// same list for a reader who never opens this file.
/// </para>
/// <para><b>This reader never writes.</b></para>
/// </remarks>
public sealed class ProjectReader
{
    private static readonly ILogger _logger = Log.ForContext<ProjectReader>();

    private const string CentralPackageVersionsFile = "Directory.Packages.props";

    /// <summary>Reads <paramref name="projectPath"/>; never throws for a bad project file.</summary>
    public ProjectReading Read(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        XDocument document;
        try
        {
            document = XDocument.Load(projectPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            // Omit, report, continue - the design's error scenario 3. One unreadable project
            // costs the graph that project, never the diagram.
            _logger.Warning(error, "Project {Project} could not be read", projectPath);
            return ProjectReading.OfFailure(projectPath, $"The project file could not be read: {error.Message}");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? string.Empty;

        return new ProjectReading(
            TargetFrameworksOf(document),
            ProjectReferencesOf(document, directory),
            PackageReferencesOf(document, directory),
            []);
    }

    /// <summary>
    /// The frameworks the project targets. <c>TargetFrameworks</c> (plural, semicolon-separated)
    /// and <c>TargetFramework</c> (singular) are both read, because multi-targeting is ordinary
    /// and a reader that knew only the singular would report nothing for a project that has
    /// several - an absence indistinguishable from a project that declares none.
    /// </summary>
    private static IReadOnlyList<string> TargetFrameworksOf(XDocument document)
    {
        var values = document
            .Descendants()
            .Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .SelectMany(element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => !value.Contains('$', StringComparison.Ordinal)) // an unexpanded property is not a framework
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values;
    }

    /// <summary>
    /// Project-to-project references, each resolved to a full path so the graph can match them
    /// against the solution's own projects rather than comparing two spellings of one path.
    /// </summary>
    private static IReadOnlyList<ProjectReferenceReading> ProjectReferencesOf(XDocument document, string directory) =>
        document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include =>
            {
                var normalized = include!.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                return new ProjectReferenceReading(include!, Path.GetFullPath(Path.Combine(directory, normalized)));
            })
            .ToArray();

    /// <summary>
    /// Package references, with a version where one can be found: written on the reference, or
    /// - the normal case in a centrally managed repository - found by walking upward.
    /// </summary>
    private IReadOnlyList<PackageReferenceReading> PackageReferencesOf(XDocument document, string directory)
    {
        var references = new List<PackageReferenceReading>();

        foreach (var element in document.Descendants().Where(candidate => candidate.Name.LocalName == "PackageReference"))
        {
            var id = (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            // A Version attribute, or a <Version> child - both are legal MSBuild.
            var version = (string?)element.Attribute("Version")
                ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version")?.Value;

            if (string.IsNullOrWhiteSpace(version))
            {
                version = CentrallyManagedVersionOf(id!, directory);
            }

            // Null rather than "" when nothing could be found: not discoverable and empty are
            // different answers, and the property grid renders them differently (Requirement 4.4).
            references.Add(new PackageReferenceReading(
                id!.Trim(),
                string.IsNullOrWhiteSpace(version) ? null : version.Trim()));
        }

        return references;
    }

    /// <summary>
    /// Walks upward from the project's folder looking for a <c>Directory.Packages.props</c> that
    /// names this package, as MSBuild's own central package management does. The first file
    /// naming it wins, which is MSBuild's nearest-wins behaviour.
    /// </summary>
    private string? CentrallyManagedVersionOf(string packageId, string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, CentralPackageVersionsFile);
            if (File.Exists(candidate))
            {
                var version = PackageVersionIn(candidate, packageId);
                if (version is not null)
                {
                    return version;
                }
            }

            directory = directory.Parent;
        }

        _logger.Debug(
            "No central version found for {Package} above {Directory}; its version is not discoverable",
            packageId,
            startDirectory);
        return null;
    }

    /// <summary>The <c>PackageVersion</c> entry naming <paramref name="packageId"/>, if any.</summary>
    private static string? PackageVersionIn(string propsPath, string packageId)
    {
        try
        {
            return XDocument.Load(propsPath)
                .Descendants()
                .Where(element => element.Name.LocalName == "PackageVersion")
                .Where(element => string.Equals((string?)element.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase))
                .Select(element => (string?)element.Attribute("Version"))
                .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            // An unreadable props file costs the versions it would have supplied, not the walk:
            // a further-up file may still name the package.
            _logger.Warning(error, "Central package versions file {Props} could not be read", propsPath);
            return null;
        }
    }
}
