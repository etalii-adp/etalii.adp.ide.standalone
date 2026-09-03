namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// The nodes and edges of one <see cref="HelmChart"/> - the relationships that are the diagram
/// (Requirement 5), resolved against what is actually in the folder.
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
/// edge when what it names finally appears (R5.2, R5.6). The <c>global:</c> key marks the
/// values node rather than fanning out one configures edge per dependency (R5.4).
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

        // The values stack: the default layer, then each override stacked onto it (R5.3).
        var defaultValues = chart.Values.FirstOrDefault(values => values.IsDefault);
        foreach (var values in chart.Values)
        {
            var id = ValuesId(values);
            nodes.Add(new HelmNode(id, HelmNodeKind.Values, values.RelativePath, values.RelativePath));
            if (!values.IsDefault && defaultValues is not null)
            {
                edges.Add(new HelmEdge(id, ValuesId(defaultValues), HelmEdgeKind.Overrides, string.Empty, OpenEnd: false));
            }
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

        // Dependencies: declared (labeled with the constraint), then resolved or an open end.
        foreach (var resolved in resolution.Dependencies)
        {
            var dependency = resolved.Dependency;
            var id = DependencyId(dependency);
            nodes.Add(new HelmNode(id, HelmNodeKind.Dependency, dependency.EffectiveName, string.Empty));
            edges.Add(new HelmEdge(chartId, id, HelmEdgeKind.Declares, dependency.VersionConstraint ?? string.Empty, OpenEnd: false));
            edges.Add(resolved.Vendored is { } vendored
                ? new HelmEdge(id, VendoredId(vendored), HelmEdgeKind.Resolves, string.Empty, OpenEnd: false)
                : new HelmEdge(id, string.Empty, HelmEdgeKind.Resolves, dependency.EffectiveName, OpenEnd: true));

            // The default layer's own top-level key configures the mounted subchart (R5.4).
            if (defaultValues is not null
                && defaultValues.TopLevelKeys.Contains(dependency.EffectiveName, StringComparer.Ordinal))
            {
                edges.Add(new HelmEdge(
                    ValuesId(defaultValues), id, HelmEdgeKind.Configures, dependency.EffectiveName, OpenEnd: false));
            }
        }

        if (chart.Lock is { } chartLock)
        {
            nodes.Add(new HelmNode($"lock:{chartLock.RelativePath}", HelmNodeKind.Lock, chartLock.RelativePath, chartLock.RelativePath));
        }

        return new HelmGraph(nodes, edges, resolution);
    }

    internal static string ValuesId(ValuesFile values) => $"values:{values.RelativePath}";

    internal static string TemplateId(TemplateFile template) => $"tpl:{template.RelativePath}";

    internal static string DependencyId(DependencyDeclaration dependency) => $"dep:{dependency.EffectiveName}";

    internal static string VendoredId(VendoredEntry entry) => $"{(entry.Sealed ? "tgz" : "sub")}:{entry.RelativePath}";

    private static string FileName(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        return slash >= 0 ? relativePath[(slash + 1)..] : relativePath;
    }
}
