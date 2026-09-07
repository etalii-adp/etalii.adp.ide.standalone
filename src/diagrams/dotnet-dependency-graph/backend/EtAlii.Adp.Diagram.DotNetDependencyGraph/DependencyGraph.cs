using System.Globalization;
using System.Text.RegularExpressions;
using Serilog;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Turns readings into the graph: one node per project, one node per package id, one edge per
/// declared reference.
/// </summary>
/// <remarks>
/// <para>
/// <b>The element id scheme is the load-bearing decision in this module</b>, because a stored
/// position is bound to one (Requirement 6.7). A project's id is its path relative to the
/// solution; a package's id is <b>the package id alone, without the version</b>. See
/// <see cref="PackageNode.Id"/> for why the version is excluded and what it costs.
/// </para>
/// <para>
/// <b>No filesystem here.</b> This takes readings and returns a graph, so the derivation is
/// testable without a disk and the readers are testable without a graph - the boundary the
/// design asked to be kept sharp.
/// </para>
/// </remarks>
public sealed class DependencyGraph
{
    private static readonly ILogger _logger = Log.ForContext<DependencyGraph>();

    /// <summary>
    /// <c>net10.0</c>, <c>net8.0</c> - the modern target frameworks that name a .NET version.
    /// Anything else (<c>netstandard2.0</c>, <c>net472</c>) names something that is not a .NET
    /// version in this sense, and yields absence rather than a guess.
    /// </summary>
    private static readonly Regex _dotNetTargetFramework = new(
        @"^net(?<version>\d+\.\d+)$",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>Derives the graph from one solution reading and the projects' own readings.</summary>
    /// <param name="solution">What the solution named and what resolved.</param>
    /// <param name="readings">
    /// Each resolved project's reading, keyed by the project's absolute path - the same key the
    /// solution's projects carry, so a <c>ProjectReference</c> can be matched to a node rather
    /// than to a second spelling of a path.
    /// </param>
    public DependencyGraphModel Derive(
        SolutionReading solution,
        IReadOnlyDictionary<string, ProjectReading> readings)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(readings);

        var failures = new List<SolutionFailure>(solution.Failures);
        var projects = new List<ProjectNode>();
        var edges = new List<DependsOnEdge>();

        // Every package id the solution asks for anywhere, with every version asked for. One
        // entry per id: the sharing Requirement 3.4 wants shown once rather than repeated.
        var versionsByPackage = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var versionlessPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Node ids by absolute project path, so a ProjectReference resolves to the node the
        // solution already carries rather than creating a second one.
        var idByProjectPath = solution.Projects.ToDictionary(
            project => project.AbsolutePath,
            project => IdOfProject(project.RelativePath),
            StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects)
        {
            if (!readings.TryGetValue(project.AbsolutePath, out var reading))
            {
                // The solution resolved it but nobody read it. Reported rather than drawn as a
                // project with no dependencies, which would be a claim the module cannot make.
                failures.Add(new SolutionFailure(project.RelativePath, "This project was not read, so its references are unknown."));
                continue;
            }

            failures.AddRange(reading.Failures);

            var id = idByProjectPath[project.AbsolutePath];
            projects.Add(new ProjectNode(
                id,
                project.Name,
                project.RelativePath,
                reading.TargetFrameworks,
                DotNetVersionOf(reading.TargetFrameworks)));

            foreach (var reference in reading.ProjectReferences)
            {
                if (!idByProjectPath.TryGetValue(reference.AbsolutePath, out var targetId))
                {
                    // Declared, but pointing outside the solution's project set. Reported, not
                    // invented: drawing a node the solution does not contain would make the
                    // graph disagree with its own subject.
                    failures.Add(new SolutionFailure(
                        reference.Include,
                        $"{project.Name} references this project, but the solution does not contain it."));
                    continue;
                }

                edges.Add(new DependsOnEdge(IdOfEdge(id, targetId), id, targetId, DependsOnKind.Project));
            }

            foreach (var reference in reading.PackageReferences)
            {
                var packageId = IdOfPackage(reference.PackageId);

                if (!versionsByPackage.TryGetValue(reference.PackageId, out var versions))
                {
                    versions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    versionsByPackage[reference.PackageId] = versions;
                }

                if (reference.Version is null)
                {
                    // Recorded separately from the versions: an undiscoverable version must not
                    // masquerade as a version, and must not be lost either.
                    versionlessPackages.Add(reference.PackageId);
                }
                else
                {
                    versions.Add(reference.Version);
                }

                var edge = new DependsOnEdge(IdOfEdge(id, packageId), id, packageId, DependsOnKind.Package);
                if (!edges.Any(existing => existing.Id == edge.Id))
                {
                    // One project declaring the same package twice - across two ItemGroups, or
                    // once per target framework - is one dependency, not two edges.
                    edges.Add(edge);
                }
            }
        }

        var packages = versionsByPackage
            .Select(entry => new PackageNode(
                IdOfPackage(entry.Key),
                entry.Key,
                [.. entry.Value],
                // A conflict is disagreement about the version: two known versions, or a known
                // one beside a reference whose version could not be discovered.
                entry.Value.Count > 1 || (entry.Value.Count > 0 && versionlessPackages.Contains(entry.Key))))
            .OrderBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.Information(
            "Derived {Projects} projects, {Packages} packages and {Edges} edges; {Failures} declarations did not resolve",
            projects.Count.ToString(CultureInfo.InvariantCulture),
            packages.Length.ToString(CultureInfo.InvariantCulture),
            edges.Count.ToString(CultureInfo.InvariantCulture),
            failures.Count.ToString(CultureInfo.InvariantCulture));

        return new DependencyGraphModel(projects, packages, edges, failures);
    }

    /// <summary>A project's element id: its path relative to the solution.</summary>
    public static string IdOfProject(string relativePath) => $"project:{relativePath}";

    /// <summary>
    /// A package's element id: <b>the package id, and never the version</b>. The whole of the
    /// design decision this module's guarding test exists to pin.
    /// </summary>
    public static string IdOfPackage(string packageId) => $"package:{packageId}";

    /// <summary>An edge's id, from its two ends, so a redrawn graph names the same edge.</summary>
    private static string IdOfEdge(string fromId, string toId) => $"depends:{fromId}->{toId}";

    /// <summary>
    /// The .NET version the frameworks name, where they name one. Absence rather than a guess
    /// for anything that is not a <c>net&lt;major&gt;.&lt;minor&gt;</c> target: a project on
    /// <c>netstandard2.0</c> has no .NET version to show, and saying so is Requirement 4.4.
    /// </summary>
    private static string? DotNetVersionOf(IReadOnlyList<string> targetFrameworks)
    {
        var versions = targetFrameworks
            .Select(framework => _dotNetTargetFramework.Match(framework))
            .Where(match => match.Success)
            .Select(match => match.Groups["version"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return versions.Length switch
        {
            0 => null,
            // Multi-targeted across two .NET versions: both, rather than one picked silently.
            _ => string.Join(", ", versions.OrderBy(version => version, StringComparer.Ordinal)),
        };
    }
}
