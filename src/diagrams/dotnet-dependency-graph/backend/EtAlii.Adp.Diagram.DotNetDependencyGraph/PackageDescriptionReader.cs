using System.Xml.Linq;
using Serilog;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Reads a package's description from the <b>local NuGet cache and nowhere else</b>: the
/// <c>.nuspec</c> beside the restored package in the global packages folder.
/// </summary>
/// <remarks>
/// <para>
/// <b>No feed. No network. No exception.</b> This is the user's recorded decision, taken over
/// the requirements' own more capable recommendation, to keep the product's "no infrastructure
/// beyond the workspace's own files" principle absolute rather than optional. The cost is
/// accepted and explicit: <b>a package that has never been restored on this machine has no
/// description</b>, and per Requirement 5.3 that is never an error, never a blocking wait, and
/// never an empty diagram.
/// </para>
/// <para>
/// <b>Absence is not emptiness.</b> A package with no cached <c>.nuspec</c> yields
/// <c>null</c> - not obtainable - while a package whose <c>.nuspec</c> carries an empty
/// description yields the empty string. The grid renders the two differently (Requirement 4.4),
/// so conflating them here would erase a distinction the requirement asks for.
/// </para>
/// <para><b>This reader never writes.</b></para>
/// </remarks>
public sealed class PackageDescriptionReader
{
    private static readonly ILogger _logger = Log.ForContext<PackageDescriptionReader>();

    private readonly string _globalPackagesFolder;

    /// <summary>Reads from the machine's own global packages folder.</summary>
    public PackageDescriptionReader()
        : this(DefaultGlobalPackagesFolder())
    {
    }

    /// <summary>Reads from a named cache root - what the tests hand a fixture cache.</summary>
    public PackageDescriptionReader(string globalPackagesFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(globalPackagesFolder);
        _globalPackagesFolder = globalPackagesFolder;
    }

    /// <summary>
    /// Where NuGet keeps restored packages: <c>NUGET_PACKAGES</c> where it is set - which is how
    /// a build agent or a repository-local cache redirects it - and the per-user default
    /// otherwise.
    /// </summary>
    public static string DefaultGlobalPackagesFolder()
    {
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");
    }

    /// <summary>
    /// The description for <paramref name="packageId"/> at any of <paramref name="versions"/>,
    /// or <c>null</c> when none of them is cached.
    /// </summary>
    /// <remarks>
    /// Several versions are tried because a package in conflict has several, and a description
    /// from any of them is better than none - the description describes the package, not the
    /// release. The first that answers wins.
    /// </remarks>
    public string? Read(string packageId, IReadOnlyList<string> versions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(versions);

        foreach (var version in versions)
        {
            var description = ReadOne(packageId, version);
            if (description is not null)
            {
                return description;
            }
        }

        // Not cached, or cached without a description. Absence, and the diagram carries on.
        return null;
    }

    /// <summary>
    /// Where a cached package's <c>.nuspec</c> would be. Exposed rather than inlined because
    /// the lowercasing below cannot be guarded through the filesystem on Windows: NTFS is
    /// case-insensitive, so a reader using the declared casing finds the file anyway and a
    /// behavioural test passes against the defect - green here, broken on a case-sensitive
    /// filesystem. Asserting the constructed path is the only form of that guard which fails
    /// on the machine the test runs on.
    /// </summary>
    public static string NuspecPathFor(string globalPackagesFolder, string packageId, string version)
    {
        // NuGet lowercases both the package folder and the .nuspec file name when it restores,
        // whatever casing the reference used.
        var id = packageId.ToLowerInvariant();
        return Path.Combine(globalPackagesFolder, id, version.ToLowerInvariant(), $"{id}.nuspec");
    }

    private string? ReadOne(string packageId, string version)
    {
        var nuspec = NuspecPathFor(_globalPackagesFolder, packageId, version);

        if (!File.Exists(nuspec))
        {
            return null;
        }

        try
        {
            // Matched by local name: a .nuspec carries one of several schema namespaces
            // depending on the vintage of the tool that wrote it, and binding to one of them
            // would make the reader answer for some packages and not others for a reason that
            // has nothing to do with the package.
            var description = XDocument.Load(nuspec)
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "description")
                ?.Value;

            return description?.Trim();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            // A cache entry mid-write, or unreadable. Absence rather than a throw: a
            // description is the least important thing on the element and must never be the
            // reason a diagram fails to open.
            _logger.Debug(error, "The cached nuspec {Nuspec} could not be read", nuspec);
            return null;
        }
    }
}
