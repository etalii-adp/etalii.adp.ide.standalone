using System.Runtime.CompilerServices;
using EtAlii.Adp.Files;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Holds one derived graph per solution, so every session and the property provider read the
/// same one rather than each deriving its own, and keeps it true as the files it was derived from
/// change - through the shared watched-folder store (code-structure FR-037).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here writes.</b> It reads a solution, the project files it names, the central
/// package versions above them and the local NuGet cache - and that is the whole of its contact
/// with the filesystem.
/// </para>
/// <para>
/// <b>Each graph is the reading of one body.</b> A solution is opened through the module's binding as a
/// <see cref="DotNetBody"/>, read by the plugin, and the body is kept with the graph it gave for as long as that
/// graph is the store's, so the graph and its body are one reading (<see cref="BodyOf"/>).
/// </para>
/// <para>
/// <b>An unreadable subject is a graph, not an exception.</b> A solution that will not parse
/// yields an empty graph carrying its reason, because the diagram opens either way
/// (Requirement 2.4) and a store that threw would take the diagram down with the file.
/// </para>
/// <para>
/// <b>What is watched is what was read</b> - the solution, every project file it resolved, and
/// every <c>Directory.Packages.props</c> above them, as <see cref="WatchedFiles"/> reports them,
/// and keyed by the solution's path rather than its folder, which can hold two. A watch over only
/// the solution would miss a reference added to a project; a watch over only project files would
/// miss a version bump, which in a centrally managed repository lands in a props file and in no
/// <c>.csproj</c> at all.
/// </para>
/// </remarks>
public sealed class DependencyGraphStore(
    SolutionReader solutions,
    ProjectReader projects,
    PackageDescriptionReader descriptions,
    TimeSpan? settleDelay = null,
    ProjectFileEvents? events = null,
    TimeProvider? time = null)
    : WatchedFolderStore<DependencyGraphModel>(settleDelay ?? DefaultSettleDelay, events, time), IDependencyGraphStore
{
    private static readonly ILogger _logger = Log.ForContext<DependencyGraphStore>();

    /// <summary>
    /// A build or a package restore rewrites many files in a burst, and a graph rebuilt per event
    /// would rebuild dozens of times for one logical change.
    /// </summary>
    private static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromMilliseconds(250);

    private readonly SolutionReader _solutions = solutions ?? throw new ArgumentNullException(nameof(solutions));
    private readonly ProjectReader _projects = projects ?? throw new ArgumentNullException(nameof(projects));
    private readonly PackageDescriptionReader _descriptions = descriptions ?? throw new ArgumentNullException(nameof(descriptions));
    private readonly ConditionalWeakTable<DependencyGraphModel, DotNetBody> _bodies = [];

    /// <summary>The body <paramref name="graph"/> was read from, when this store read it; null for any other graph.</summary>
    public DotNetBody? BodyOf(DependencyGraphModel graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return _bodies.TryGetValue(graph, out var body) ? body : null;
    }

    /// <summary>
    /// Every file whose change should make this graph stale: the solution, each project file it
    /// resolved, and every <c>Directory.Packages.props</c> above them. The store watches this
    /// rather than guessing, so a graph derived from a file is refreshed by that file.
    /// </summary>
    protected override IReadOnlyCollection<string> WatchedFiles(string folder, DependencyGraphModel snapshot)
    {
        var full = IoPath.GetFullPath(folder);
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

    /// <summary>Derives the graph of the solution at <paramref name="folder"/>, which names the solution file.</summary>
    protected override DependencyGraphModel Read(string folder) => Derive(folder);

    private DependencyGraphModel Derive(string solutionPath)
    {
        var body = DotNetBody.Open(solutionPath, _solutions, _projects, _descriptions);
        _bodies.AddOrUpdate(body.Graph, body);

        _logger.Information("Derived the dependency graph for {Solution}", solutionPath);

        return body.Graph;
    }
}
