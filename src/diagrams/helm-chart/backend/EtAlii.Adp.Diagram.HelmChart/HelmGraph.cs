namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// The nodes of one <see cref="HelmChart"/>, and the two relationships that may have an open end,
/// <c>Resolves</c> and <c>Includes</c>, resolved against what is actually in the folder.
/// </summary>
/// <remarks>
/// <para>
/// Pure: a chart in, a graph out, no file access and no clock. Everything it needs was read
/// once by <see cref="HelmChartReader"/>, which is why the diagram and the problems panel can
/// never disagree about what is in the folder.
/// </para>
/// <para>
/// An open end is a state, never an exception: an Unvendored dependency and an include no
/// local partial defines are both drawn with their identity intact, so the edge stays the same
/// edge when what it names finally appears (R5.2, R5.6). DISL cannot derive a relation without a
/// target (gap 7 of <c>definition/helm-chart.md</c>), so these two are made here and handed to the
/// definition as stored relations.
/// </para>
/// <para>
/// <b><c>Declares</c>, <c>Overrides</c> and <c>Configures</c> are not made here</b>: they always have
/// both ends, and the bundled definition derives them (helm-chart-disl-fbl, task 6). What is left of
/// this class leaves the product with the reading through the binding (task 9).
/// </para>
/// </remarks>
public sealed class HelmGraph
{
    private HelmGraph(IReadOnlyList<HelmNode> nodes, IReadOnlyList<HelmEdge> edges, DependencyResolutionResult resolution)
    {
        Nodes = nodes;
        Edges = edges;
        Resolution = resolution;
    }

    public IReadOnlyList<HelmNode> Nodes { get; }

    /// <summary>The <c>Includes</c> and <c>Resolves</c> relations, in reading order; the definition derives the other three.</summary>
    public IReadOnlyList<HelmEdge> Edges { get; }

    /// <summary>The matcher's account, kept beside the edges: validation reads Undeclared from here.</summary>
    public DependencyResolutionResult Resolution { get; }

    /// <summary>The node with that id, or null.</summary>
    public HelmNode? Node(string id) => Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));

    public static HelmGraph Derive(HelmChart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var nodes = new List<HelmNode>();
        var edges = new List<HelmEdge>();
        var resolution = DependencyResolution.Match(chart.Dependencies, chart.Vendored);

        if (!chart.IsChart)
        {
            // The registered non-chart: an empty canvas, not an error page (Requirement 10.1).
            return new HelmGraph(nodes, edges, resolution);
        }

        const string chartId = "chart";
        nodes.Add(new HelmNode(chartId, HelmNodeKind.Chart, chart.Metadata?.Name ?? "(chart)", "Chart.yaml"));

        // The values stack: the default layer and each override, which the definition stacks onto it (R5.3).
        foreach (var values in chart.Values)
        {
            nodes.Add(new HelmNode(ValuesId(values), HelmNodeKind.Values, values.RelativePath, values.RelativePath));
        }

        if (chart.Schema is { } schema)
        {
            nodes.Add(new HelmNode($"schema:{schema.RelativePath}", HelmNodeKind.Schema, schema.RelativePath, schema.RelativePath));
        }

        // Templates - every role is a node; partials are what includes edges land on (R5.6).
        var partials = chart.Templates.Where(template => template.Role == TemplateRole.Partial).ToArray();
        foreach (var template in chart.Templates)
        {
            nodes.Add(new HelmNode(
                TemplateId(template), HelmNodeKind.Template, FileName(template.RelativePath), template.RelativePath));
        }

        foreach (var template in chart.Templates)
        {
            foreach (var reference in template.Facts.References)
            {
                var defining = partials.FirstOrDefault(partial =>
                    partial.Facts.Defines.Contains(reference, StringComparer.Ordinal));
                edges.Add(defining is not null
                    ? new HelmEdge(TemplateId(template), TemplateId(defining), HelmEdgeKind.Includes, reference, OpenEnd: false)
                    : new HelmEdge(TemplateId(template), string.Empty, HelmEdgeKind.Includes, reference, OpenEnd: true));
            }
        }

        if (chart.Crds is not null)
        {
            nodes.Add(new HelmNode("crds", HelmNodeKind.Crds, "crds", "crds"));
        }

        // What is actually vendored, whether or not anything declares it (Undeclared is
        // validation's to warn about, but the content is still drawn).
        foreach (var entry in chart.Vendored)
        {
            nodes.Add(new HelmNode(
                VendoredId(entry),
                entry.Sealed ? HelmNodeKind.Archive : HelmNodeKind.Subchart,
                entry.EntryName,
                entry.RelativePath));
        }

        // Dependencies, each resolved or an open end; the definition declares them from the chart (R5.1).
        foreach (var resolved in resolution.Dependencies)
        {
            var dependency = resolved.Dependency;
            var id = DependencyId(dependency);
            nodes.Add(new HelmNode(id, HelmNodeKind.Dependency, dependency.EffectiveName, string.Empty));
            edges.Add(resolved.Vendored is { } vendored
                ? new HelmEdge(id, VendoredId(vendored), HelmEdgeKind.Resolves, string.Empty, OpenEnd: false)
                : new HelmEdge(id, string.Empty, HelmEdgeKind.Resolves, dependency.EffectiveName, OpenEnd: true));
        }

        if (chart.Lock is { } chartLock)
        {
            nodes.Add(new HelmNode($"lock:{chartLock.RelativePath}", HelmNodeKind.Lock, chartLock.RelativePath, chartLock.RelativePath));
        }

        return new HelmGraph(nodes, edges, resolution);
    }

    private static string ValuesId(ValuesFile values) => $"values:{values.RelativePath}";

    private static string TemplateId(TemplateFile template) => $"tpl:{template.RelativePath}";

    internal static string DependencyId(DependencyDeclaration dependency) => $"dep:{dependency.EffectiveName}";

    private static string VendoredId(VendoredEntry entry) => $"{(entry.Sealed ? "tgz" : "sub")}:{entry.RelativePath}";

    private static string FileName(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        return slash >= 0 ? relativePath[(slash + 1)..] : relativePath;
    }
}
