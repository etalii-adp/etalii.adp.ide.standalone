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

    /// <summary>
    /// The share of a solution's projects at which a package stops discriminating anything and
    /// becomes background: referenced by this fraction or more, it is marked ambient and the
    /// canvas hides it by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured, and the measurement is the whole argument.</b> Against this repository's own
    /// <c>EtAlii.Adp.slnx</c> (2026-09-08): 104 projects, 17 packages, 427 edges of which 155 are
    /// package references. The degrees are
    /// <c>26 26 26 19 18 14 6 6 4 2 2 1 1 1 1 1 1</c> - so <b>four package nodes carry 97 of the
    /// 155 package edges, 63% of them</b>, and they discriminate nothing: an edge present on 26
    /// of 104 projects says <i>this is a test project</i>, which the project's own name already
    /// says. <b>A near-universal edge is noise wearing the shape of information.</b>
    /// </para>
    /// <para>
    /// <b>The node count was never the problem.</b> 119 nodes laid out by depth is busy, not
    /// unreadable. So this is not a limit and not a grouping: <b>nothing here is too big, only
    /// too uniformly connected</b>, and truncation would lose real structure to fix a problem it
    /// is not the shape of.
    /// </para>
    /// <para>
    /// <b>Degree rather than a curated list of build and test package names</b>, deliberately. A
    /// name list needs maintaining and is wrong on the first repository that is not this one.
    /// Degree is a property of the subject rather than of our opinion about names - and it has a
    /// consequence worth stating rather than discovering: <b>at this repository no threshold can
    /// separate <c>Grpc.Tools</c> (19) from <c>Serilog</c> (18)</b>, so a rule catching the one
    /// catches the other. That is the rule working. Fifteen percent hides five packages here and
    /// keeps twelve; nothing is lost, because hidden is a view state the canvas states and
    /// offers back.
    /// </para>
    /// <para>
    /// <b>Why fifteen percent and not something that sounds like "near-universal".</b> The hubs
    /// sit at a quarter of the projects and the fourth of them at 18%, so a threshold that
    /// catches what the measurement identifies has to be well below universal. The marking is
    /// called <i>ambient</i> rather than <i>near-universal</i> for that reason: a reader meeting
    /// a package on 16 of 104 projects would rightly reject the stronger word, and the property
    /// being measured is background-ness rather than ubiquity.
    /// </para>
    /// </remarks>
    public const double AmbientShare = 0.15;

    /// <summary>
    /// The fewest dependents a package can have and still be ambient, whatever the share says.
    /// </summary>
    /// <remarks>
    /// <b>Below a handful of dependents there is no crowd to disappear into.</b> The share alone
    /// makes a seven-project solution hide a package two of them use, which is not background,
    /// it is a third of the solution - and the crowding this exists to relieve does not occur at
    /// that size. This floor is what keeps a rule measured on 104 projects from misfiring on
    /// four, and it is why the shipped four-project example hides nothing at all.
    /// </remarks>
    public const int AmbientFloor = 5;

    // WHY THIS RULE IS ABOUT PACKAGES, AND MUST STAY ABOUT PACKAGES.
    //
    // "Why not projects too" is the first question a reader has, and it is measured rather than
    // argued. The two highest-degree nodes in this repository's own graph are BOTH PROJECTS, and
    // both beat every package hub:
    //
    //   out-degree 87   EtAlii.Adp.Backend.Service   the composition root (project edges only;
    //                                                 89 counting its two package references)
    //   in-degree  65   EtAlii.Adp.Diagram           the contract every module implements
    //   in-degree  26   xunit.v3 and the other two test-harness packages
    //
    // A degree rule applied to every node would hide both. And they carry the OPPOSITE
    // information from a package hub at the same degree. `xunit.v3` on 26 test projects tells
    // you nothing the project names do not; `Backend.Service` referencing 87 modules tells you
    // exactly how this application is assembled, and `EtAlii.Adp.Diagram` with 65 dependents is
    // the module contract itself. SAME DEGREE SIGNATURE, OPPOSITE INFORMATION CONTENT.
    //
    // The in-degree line is the one that matters, because it closes the obvious escape. It is
    // tempting to answer "the rule measures how many projects reference a thing, so a
    // composition root's outgoing edges were never at risk" - and that would be true of
    // Backend.Service alone. EtAlii.Adp.Diagram is a project with an IN-degree of 65, measured
    // exactly as a package's is, and larger than any of them. So the scoping is doing real work
    // rather than being incidentally safe.
    //
    // Both figures were invisible until wildcard ProjectReferences were expanded (task 3): read
    // literally, the composition root declares two references rather than 87.
    //
    // A note on the numbers themselves, because getting them wrong is how this paragraph was
    // written twice. DIRECTION AND EDGE KIND ARE PART OF WHAT A DEGREE COUNTS. The first draft
    // set an out-degree of 89 against an in-degree of 65 - and the 89 mixed project and package
    // edges while the 65 was project edges alone, so it compared two different quantities twice
    // over. Both drafts read plausibly. Say which direction and which kind before quoting a
    // degree here.

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
                if (edges.All(existing => existing.Id != edge.Id))
                {
                    // One project declaring the same package twice - across two ItemGroups, or
                    // once per target framework - is one dependency, not two edges.
                    edges.Add(edge);
                }
            }
        }

        // How many projects reference each package. One edge per project per package by
        // construction above, so counting edges counts projects.
        var dependents = edges
            .Where(edge => edge.Kind == DependsOnKind.Package)
            .GroupBy(edge => edge.ToElementId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        // The threshold in projects rather than in a fraction, so the comparison below is
        // integer and the number can be stated. Ceiling, so the share is a floor rather than
        // something a rounding could slip under.
        var ambientAt = Math.Max(AmbientFloor, (int)Math.Ceiling(projects.Count * AmbientShare));

        var packages = versionsByPackage
            .Select(entry =>
            {
                var id = IdOfPackage(entry.Key);
                var dependentCount = dependents.GetValueOrDefault(id);
                return new PackageNode(
                    id,
                    entry.Key,
                    [.. entry.Value],
                    // A conflict is disagreement about the version: two known versions, or a
                    // known one beside a reference whose version could not be discovered.
                    entry.Value.Count > 1 || (entry.Value.Count > 0 && versionlessPackages.Contains(entry.Key)),
                    Description: null,
                    DependentProjectCount: dependentCount,
                    IsAmbient: dependentCount >= ambientAt);
            })
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
