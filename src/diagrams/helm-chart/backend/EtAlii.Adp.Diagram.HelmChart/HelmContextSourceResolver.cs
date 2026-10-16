using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// Makes a chart node selectable: resolves an <c>element_id</c> against the chart whose
/// registration the selection sits in, verifies the node really is in that chart, and fills in
/// the detail a consumer shows (Requirements 8, 9.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>The steps every resolver repeats are the base's</b> (code-structure FR-032). This says where
/// a chart is read from and what its elements are called: the folder the registration sits in,
/// through the shared watched-folder store, and a node by its own artifact's path, an edge by the
/// side that declares it.
/// </para>
/// <para>
/// <b>The target is the registration</b>, as every diagram's is the file it was selected in; the
/// chart folder is one <c>GetDirectoryName</c> away, and the stored positions the canvas was drawn
/// with are in that file.
/// </para>
/// </remarks>
public sealed class HelmContextSourceResolver(DiagramFileRouter router, IHelmChartStore store, HelmElementMapper mapper)
    : DiagramSnapshotSourceResolver<HelmContextSourceResolver.Selected>(router)
{
    /// <summary>A selected diagram: its chart, the graph derived from it, and the registration it was selected in.</summary>
    public sealed record Selected(HelmChart Chart, HelmGraph Graph, string Registration);

    /// <inheritdoc />
    protected override DiagramOrigin Origin => Diagram.HelmCharts.Origin;

    /// <inheritdoc />
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a Helm chart diagram.",
        "Unknown element.",
        "New element",
        "New relation");

    /// <inheritdoc />
    protected override Selected Load(string bodyPath)
    {
        var chart = store.GetOrLoad(FolderOf(bodyPath));
        return new Selected(chart, HelmGraph.Derive(chart), bodyPath);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A node answers with its own artifact's path (a dependency, having no file of its own,
    /// answers as the file that declares it), and an edge answers as its declaring side - the
    /// file a reader asking "why is this here" opens.
    /// </remarks>
    protected override (IReadOnlyList<string> Path, string Text)? Describe(Selected snapshot, string elementId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var chart = snapshot.Chart;
        var graph = snapshot.Graph;

        if (graph.Node(elementId) is { } node)
        {
            var path = node.RelativePath.Length > 0 ? node.RelativePath : DeclaringFile(chart);
            return (Segments(path), node.Name);
        }

        // A relation is the definition's model's: two of its kinds are read, three are derived (task 6).
        var relation = HelmDefinition.ModelOf(chart).Relations.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
        if (relation is null)
        {
            return null;
        }

        var sourcePath = relation.Source is { } source ? graph.Node(source.Id)?.RelativePath : null;
        var declaredIn = sourcePath is { Length: > 0 } ? sourcePath : DeclaringFile(chart);
        var verb = Verb(relation.Type.Name);
        var label = relation.ValueOf("label") is string { Length: > 0 } says ? $"{verb} {says}" : verb;
        return (Segments(declaredIn), label);
    }

    /// <inheritdoc />
    /// <remarks>Drawn as the canvas draws it: the whole chart, arranged with the positions the registration stores.</remarks>
    protected override DiagramElement? Drawn(Selected snapshot, string elementId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var chart = snapshot.Chart;
        var graph = snapshot.Graph;
        var boxes = HelmLayout.Arranged(chart, graph, RegistrationLayout.Read(snapshot.Registration));
        return mapper.Elements(chart, graph, boxes)
            .FirstOrDefault(element => string.Equals(element.Id, elementId, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    /// <remarks>
    /// After every change to the chart folder: a deleted values file clears the selection, a
    /// change to what an edge names re-announces it (Requirement 8.3). The folder watched is the
    /// registration's own - one further up would never hear a thing.
    /// </remarks>
    protected override IDisposable Follow(ShortGuid watchId, string bodyPath, string elementId, Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        return store.Watch(FolderOf(bodyPath), _ => changed());
    }

    /// <summary>Where dependencies are declared: <c>Chart.yaml</c>, or <c>requirements.yaml</c> for a legacy chart.</summary>
    private static string DeclaringFile(HelmChart chart) => chart.Legacy ? "requirements.yaml" : "Chart.yaml";

    /// <summary>What a relation of the definition's type does, as a reader says it.</summary>
    private static string Verb(string type) => type switch
    {
        "Declares" => "declares",
        "Resolves" => "resolves to",
        "Overrides" => "overrides",
        "Configures" => "configures",
        "Includes" => "includes",
        _ => "",
    };

    /// <summary>
    /// The folder a registration marks. The <c>.adp</c> routes as its own body for a type with
    /// no extension, so the body is the registration itself.
    /// </summary>
    private static string FolderOf(string registrationPath) =>
        IoPath.GetDirectoryName(IoPath.GetFullPath(registrationPath))
        ?? throw new InvalidOperationException($"'{registrationPath}' has no folder to read.");

    private static string[] Segments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
}
