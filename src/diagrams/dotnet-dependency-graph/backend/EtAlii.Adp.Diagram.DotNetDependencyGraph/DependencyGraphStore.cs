using System.Collections.Concurrent;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Holds one derived graph per solution, so every session and the property provider read the
/// same one rather than each deriving its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here writes.</b> It reads a solution, the project files it names, the central
/// package versions above them and the local NuGet cache - and that is the whole of its contact
/// with the filesystem.
/// </para>
/// <para>
/// <b>An unreadable subject is a graph, not an exception.</b> A solution that will not parse
/// yields an empty graph carrying its reason, because the diagram opens either way
/// (Requirement 2.4) and a store that threw would take the diagram down with the file.
/// </para>
/// </remarks>
public sealed class DependencyGraphStore : IDependencyGraphStore
{
    private static readonly ILogger _logger = Log.ForContext<DependencyGraphStore>();

    private readonly SolutionReader _solutions;
    private readonly ProjectReader _projects;
    private readonly PackageDescriptionReader _descriptions;
    private readonly DependencyGraph _graphs = new();

    /// <summary>Keyed by the solution's full path, so two spellings of one path share a graph.</summary>
    private readonly ConcurrentDictionary<string, DependencyGraphModel> _cache = new(StringComparer.OrdinalIgnoreCase);

    public DependencyGraphStore(SolutionReader solutions, ProjectReader projects, PackageDescriptionReader descriptions)
    {
        ArgumentNullException.ThrowIfNull(solutions);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(descriptions);
        _solutions = solutions;
        _projects = projects;
        _descriptions = descriptions;
    }

    /// <summary>The graph for <paramref name="solutionPath"/>, derived once and kept.</summary>
    public DependencyGraphModel GetOrLoad(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        return _cache.GetOrAdd(IoPath.GetFullPath(solutionPath), Derive);
    }

    /// <summary>
    /// Derives afresh and replaces what is held - the refresh of Requirement 7.1, whether it
    /// was asked for by a watcher or by the user.
    /// </summary>
    public DependencyGraphModel Reload(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);

        var full = IoPath.GetFullPath(solutionPath);
        var graph = Derive(full);
        _cache[full] = graph;
        return graph;
    }

    /// <summary>
    /// Every file whose change should make this graph stale: the solution, each project file it
    /// resolved, and every <c>Directory.Packages.props</c> above them. The watcher reads this
    /// rather than guessing, so a graph derived from a file is refreshed by that file.
    /// </summary>
    public IReadOnlyList<string> WatchedFiles(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);

        var full = IoPath.GetFullPath(solutionPath);
        var reading = _solutions.Read(full);
        var files = new List<string> { full };

        foreach (var project in reading.Projects)
        {
            files.Add(project.AbsolutePath);

            // The central versions files above each project: a version bump lands in one of
            // these and in no .csproj at all, so a watcher that knew only project files would
            // show a stale version until something else happened to change.
            var directory = new DirectoryInfo(IoPath.GetDirectoryName(project.AbsolutePath) ?? string.Empty);
            while (directory is not null)
            {
                var props = IoPath.Combine(directory.FullName, "Directory.Packages.props");
                if (File.Exists(props))
                {
                    files.Add(props);
                }

                directory = directory.Parent;
            }
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private DependencyGraphModel Derive(string solutionPath)
    {
        var solution = _solutions.Read(solutionPath);

        var readings = new Dictionary<string, ProjectReading>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in solution.Projects)
        {
            readings[project.AbsolutePath] = _projects.Read(project.AbsolutePath);
        }

        var graph = _graphs.Derive(solution, readings);

        // Descriptions last, and separately: they come from outside the workspace entirely - the
        // machine's package cache - so a graph is complete without them and merely less
        // informative. A package the cache has never seen keeps a null description, which the
        // property grid renders as an explicit absence.
        var described = graph.Packages
            .Select(package => package with { Description = _descriptions.Read(package.PackageId, package.Versions) })
            .ToArray();

        _logger.Information("Derived the dependency graph for {Solution}", solutionPath);

        return graph with { Packages = described };
    }
}
