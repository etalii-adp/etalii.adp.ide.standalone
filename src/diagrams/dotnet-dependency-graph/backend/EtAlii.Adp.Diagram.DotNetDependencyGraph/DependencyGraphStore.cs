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
/// <b>What is watched is what the reading depends on</b>, and the plugin says what that is
/// (Requirement 4.1): the solution, and the body's <see cref="DotNetBody.WatchPaths"/> - every project
/// file read, every folder a wildcard <c>Include</c> listed, and a <c>Directory.Packages.props</c> in
/// every folder above each project, there or not. Keyed by the solution's path rather than its folder,
/// which can hold two, and asked again after every read, so the watch follows the reading. A watch over
/// only the files read would miss a project a wildcard begins to match and a versions file created
/// after the diagram opened (finding B15).
/// </para>
/// <para>
/// <b>The settle is the binding's</b> (<see cref="DotNetSolutionSubject.Settle"/>, Requirement 4.2): a
/// build or a package restore rewrites many files in a burst, and a graph rebuilt per event would
/// rebuild dozens of times for one logical change. A watcher overflow is a change, in the shared watcher.
/// </para>
/// </remarks>
public sealed class DependencyGraphStore(
    SolutionReader solutions,
    ProjectReader projects,
    PackageDescriptionReader descriptions,
    TimeSpan? settleDelay = null,
    ProjectFileEvents? events = null,
    TimeProvider? time = null)
    : WatchedFolderStore<DependencyGraphModel>(settleDelay ?? DotNetSolutionSubject.Settle, events, time), IDependencyGraphStore
{
    private static readonly ILogger _logger = Log.ForContext<DependencyGraphStore>();

    private readonly SolutionReader _solutions = solutions ?? throw new ArgumentNullException(nameof(solutions));
    private readonly ProjectReader _projects = projects ?? throw new ArgumentNullException(nameof(projects));
    private readonly PackageDescriptionReader _descriptions = descriptions ?? throw new ArgumentNullException(nameof(descriptions));
    private readonly ConditionalWeakTable<DependencyGraphModel, DotNetBody> _bodies = [];

    /// <summary>The body <paramref name="graph"/> was read from, when this store read it; for any other graph, the graph as the plugin would deliver it (<see cref="DotNetBody.Of"/>).</summary>
    public DotNetBody BodyOf(DependencyGraphModel graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return _bodies.TryGetValue(graph, out var body) ? body : DotNetBody.Of(graph);
    }

    /// <summary>The solution, and what the plugin said its reading depends on beyond it.</summary>
    protected override IReadOnlyCollection<string> WatchedFiles(string folder, DependencyGraphModel snapshot) =>
        [IoPath.GetFullPath(folder), .. BodyOf(snapshot).WatchPaths];

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
