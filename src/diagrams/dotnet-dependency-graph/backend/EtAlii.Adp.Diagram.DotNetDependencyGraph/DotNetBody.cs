using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Plugins;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// One solution opened through the module's binding, <c>dotnet-dependency-graph.fbl#solution</c>: the host's side
/// of FBL 11.3. It reads the solution's bytes, opens them as a <see cref="PluginBody"/> read by a
/// <see cref="DotNetSolutionPlugin"/> over this solution's <see cref="DotNetSolutionSubject"/>, and builds the DISL
/// model of the reading with the shared <see cref="DislModelBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A solution that does not exist, or cannot be read, is not handed to <see cref="PluginBody"/></b>: the graph is
/// empty with today's sentence, and no <c>fbl.missing-body</c> is raised (Q2). Nothing here throws on content.
/// </para>
/// <para>
/// <b>The property rows are derived from <see cref="Disl"/></b>: <c>DotNetDefinition.Rows</c> reads this model and no
/// other, and the builder parity test holds it equal, element for element, to the hand-built oracle of the test project.
/// A graph no solution gave, as a test builds one, is read as the plugin would deliver it (<see cref="Of"/>).
/// </para>
/// </remarks>
public sealed class DotNetBody
{
    /// <summary>The embedded binding's name in the module's FBL document.</summary>
    private const string BindingName = "solution";

    private static readonly Lazy<FblBinding> LoadedBinding = new(() => BundledBinding.Load(typeof(DotNetBody).Assembly, "dotnet-dependency-graph.fbl", BindingName));

    private readonly Lazy<DislModel> _disl;

    private DotNetBody(string solutionPath, DependencyGraphModel graph, FblModel reading)
    {
        SolutionPath = solutionPath;
        Graph = graph;
        Reading = reading;
        _disl = new Lazy<DislModel>(() => DislModelBuilder.From(Reading, DotNetDefinition.Specification));
    }

    /// <summary>The solution's full path; empty for a body of a graph no solution gave (<see cref="Of"/>).</summary>
    public string SolutionPath { get; }

    /// <summary>The graph the plugin derived, with the failures that travel with it.</summary>
    public DependencyGraphModel Graph { get; }

    /// <summary>What the plugin read: an element per project, package and reference, each with its source.</summary>
    public FblModel Reading { get; }

    /// <summary>The DISL model the shared builder builds from <see cref="Reading"/>, once, when first asked.</summary>
    public DislModel Disl => _disl.Value;

    /// <summary>Opens the solution at <paramref name="solutionPath"/>, reading the files it leads to with the given readers; never throws on content.</summary>
    public static DotNetBody Open(string solutionPath, SolutionReader solutions, ProjectReader projects, PackageDescriptionReader descriptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        var subject = new DotNetSolutionSubject(solutionPath, solutions, projects, descriptions);
        var path = subject.SolutionPath;

        if (!File.Exists(path))
        {
            return Failed(path, SolutionReader.Absent(path));
        }

        byte[] bytes;
        try
        {
            // SharedDocumentReader rather than File.ReadAllBytes: the raw API opens at FileShare.Read and
            // loses to a concurrent save, so a solution being written by an IDE as the diagram opens
            // would read as unreadable.
            bytes = SharedDocumentReader.ReadAllBytes(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Failed(path, SolutionReader.Unreadable(path, error));
        }

        var plugin = new DotNetSolutionPlugin(subject);
        var body = PluginBody.Open(bytes, LoadedBinding.Value, plugin, Path.GetFileName(path));
        return new DotNetBody(path, plugin.Graph, body.Model);
    }

    /// <summary>
    /// The body of <paramref name="graph"/> when no solution was read for it, as a test builds one: the plugin's elements of the
    /// graph, with no source, and the model the shared builder builds from them.
    /// </summary>
    public static DotNetBody Of(DependencyGraphModel graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new DotNetBody(string.Empty, graph, new FblModel(DotNetSolutionPlugin.Elements(graph), [], false));
    }

    /// <summary>The body of a solution that was not read: the graph of its failure, and nothing read.</summary>
    private static DotNetBody Failed(string path, SolutionReading failure) =>
        new(path, new DependencyGraph().Derive(failure, new Dictionary<string, ProjectReading>()), new FblModel([], [], false));
}
