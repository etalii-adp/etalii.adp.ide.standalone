using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Specification.Disl;
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
/// through the module's store of open folders, and a node by its own artifact's path, an edge by the
/// side that declares it. Both are read from the definition's model of the folder's last reading.
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
    /// <summary>A selected diagram: the folder's last reading, and the registration it was selected in.</summary>
    public sealed record Selected(HelmReading Reading, string Registration);

    /// <inheritdoc />
    protected override DiagramOrigin Origin => Diagram.HelmCharts.Origin;

    /// <inheritdoc />
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a Helm chart diagram.",
        "Unknown element.",
        "New element",
        "New relation");

    /// <inheritdoc />
    protected override Selected Load(string bodyPath) =>
        new(store.GetOrLoad(FolderOf(bodyPath), bodyPath).Current, bodyPath);

    /// <inheritdoc />
    /// <remarks>
    /// A node answers with its own artifact's path (a dependency, having no file of its own,
    /// answers as the file that declares it), and an edge answers as its declaring side - the
    /// file a reader asking "why is this here" opens.
    /// </remarks>
    protected override (IReadOnlyList<string> Path, string Text)? Describe(Selected snapshot, string elementId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagram = snapshot.Reading.Disl.Diagram;

        if (Node(diagram, elementId) is { } node)
        {
            var path = HelmElementMapper.PathOf(node);
            return (Segments(path.Length > 0 ? path : DeclaringFile(diagram)), HelmElementMapper.NameOf(node));
        }

        // A relation is the definition's model's: two of its kinds are read, three are derived (task 6).
        var relation = diagram.Relations.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
        if (relation is null)
        {
            return null;
        }

        var sourcePath = relation.Source is { } source && Node(diagram, source.Id) is { } from ? HelmElementMapper.PathOf(from) : null;
        var declaredIn = sourcePath is { Length: > 0 } ? sourcePath : DeclaringFile(diagram);
        var verb = Verb(relation.Type.Name);
        var label = relation.ValueOf("label") is string { Length: > 0 } says ? $"{verb} {says}" : verb;
        return (Segments(declaredIn), label);
    }

    /// <inheritdoc />
    /// <remarks>Drawn as the canvas draws it: the whole chart, arranged with the positions the registration stores.</remarks>
    protected override DiagramElement? Drawn(Selected snapshot, string elementId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagram = snapshot.Reading.Disl.Diagram;
        var boxes = HelmLayout.Arranged(diagram, RegistrationLayout.Read(snapshot.Registration));
        return mapper.Elements(diagram, boxes)
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
        return store.Watch(FolderOf(bodyPath), bodyPath, _ => changed());
    }

    /// <summary>The first node with the id <paramref name="elementId"/>: two dependencies of one effective name share one.</summary>
    private static DislElement? Node(DislDiagram diagram, string elementId) =>
        diagram.Nodes.FirstOrDefault(node => string.Equals(node.Id, elementId, StringComparison.Ordinal));

    /// <summary>Where dependencies are declared: <c>Chart.yaml</c>, or <c>requirements.yaml</c> for a legacy chart (the chart's derived <c>legacy</c>).</summary>
    private static string DeclaringFile(DislDiagram diagram) =>
        diagram.NodesOfType("Chart").FirstOrDefault()?.ValueOf("legacy") is true ? "requirements.yaml" : "Chart.yaml";

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
