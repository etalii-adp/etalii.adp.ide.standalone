namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// The computed layout (Requirement 6.1): the chart's anatomy in bands, deterministic for a
/// given model, pure - no randomness, no clock, no filesystem.
/// </summary>
/// <remarks>
/// Five columns, left to right: the metadata band (chart, lock, schema, crds), the values
/// stack (the default layer first, overrides beneath it - the stack as a reader thinks of
/// it), the templates (manifests first, then partials, notes and test hooks), the declared
/// dependencies, and what <c>charts/</c> actually holds - so resolves edges run short and to
/// the right. Stored positions from the registration's <c>layout:</c> block win over all of
/// this element by element; that overlay is the session's job, not the layout's.
/// </remarks>
public static class HelmLayout
{
    private const double Left = 40;
    private const double Top = 40;
    private const double ColumnGap = 60;
    private const double RowGap = 24;

    public static IReadOnlyDictionary<string, HelmBox> Compute(HelmChart chart, HelmGraph graph)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(graph);

        var boxes = new Dictionary<string, HelmBox>(StringComparer.Ordinal);

        var metadata = graph.Nodes
            .Where(node => node.Kind is HelmNodeKind.Chart or HelmNodeKind.Lock or HelmNodeKind.Schema or HelmNodeKind.Crds)
            .OrderBy(Rank)
            .ToArray();
        var values = graph.Nodes
            .Where(node => node.Kind == HelmNodeKind.Values)
            .OrderBy(node => IsDefaultValues(chart, node) ? 0 : 1)
            .ThenBy(node => node.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var templates = graph.Nodes
            .Where(node => node.Kind == HelmNodeKind.Template)
            .OrderBy(node => RoleRank(chart, node))
            .ThenBy(node => node.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var dependencies = graph.Nodes
            .Where(node => node.Kind == HelmNodeKind.Dependency)
            .OrderBy(node => node.Name, StringComparer.Ordinal)
            .ToArray();
        var vendored = graph.Nodes
            .Where(node => node.Kind is HelmNodeKind.Subchart or HelmNodeKind.Archive)
            .OrderBy(node => node.RelativePath, StringComparer.Ordinal)
            .ToArray();

        var x = Left;
        x = Column(boxes, metadata, x);
        x = Column(boxes, values, x);
        x = Column(boxes, templates, x);
        x = Column(boxes, dependencies, x);
        Column(boxes, vendored, x);

        return boxes;
    }

    private static double Column(Dictionary<string, HelmBox> boxes, IReadOnlyList<HelmNode> nodes, double x)
    {
        var y = Top;
        double widest = 0;
        foreach (var node in nodes)
        {
            var (width, height) = SizeOf(node.Kind);
            boxes[node.Id] = new HelmBox(x, y, width, height);
            y += height + RowGap;
            widest = Math.Max(widest, width);
        }

        // An empty column takes no room, so a chart without crds or vendored content does not
        // carry a hole where they would have been.
        return widest > 0 ? x + widest + ColumnGap : x;
    }

    private static (double Width, double Height) SizeOf(HelmNodeKind kind) => kind switch
    {
        HelmNodeKind.Chart => (220, 110),
        HelmNodeKind.Values => (200, 72),
        HelmNodeKind.Schema => (200, 48),
        HelmNodeKind.Template => (240, 60),
        HelmNodeKind.Crds => (200, 48),
        HelmNodeKind.Dependency => (210, 96),
        HelmNodeKind.Subchart => (210, 84),
        HelmNodeKind.Archive => (210, 52),
        HelmNodeKind.Lock => (200, 56),
        _ => (200, 60),
    };

    private static int Rank(HelmNode node) => node.Kind switch
    {
        HelmNodeKind.Chart => 0,
        HelmNodeKind.Lock => 1,
        HelmNodeKind.Schema => 2,
        HelmNodeKind.Crds => 3,
        _ => 4,
    };

    private static bool IsDefaultValues(HelmChart chart, HelmNode node) =>
        chart.Values.FirstOrDefault(values =>
            string.Equals(values.RelativePath, node.RelativePath, StringComparison.Ordinal))?.IsDefault ?? false;

    private static int RoleRank(HelmChart chart, HelmNode node) =>
        chart.Templates.FirstOrDefault(template =>
            string.Equals(template.RelativePath, node.RelativePath, StringComparison.Ordinal))?.Role switch
        {
            TemplateRole.Manifest => 0,
            TemplateRole.Partial => 1,
            TemplateRole.Notes => 2,
            TemplateRole.Test => 3,
            _ => 4,
        };
}
