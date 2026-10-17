using EtAlii.Adp.Layout;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// The computed layout (Requirement 6.1): the chart's anatomy in bands, deterministic for a
/// given model, pure - no randomness, no clock, no filesystem.
/// </summary>
/// <remarks>
/// <para>
/// Five columns, left to right: the metadata band (chart, lock, schema, crds), the values
/// stack (the default layer first, overrides beneath it - the stack as a reader thinks of
/// it), the templates (manifests first, then partials, notes and test hooks), the declared
/// dependencies, and what <c>charts/</c> actually holds - so resolves edges run short and to
/// the right. Stored positions from the registration's <c>layout:</c> block win over all of
/// this element by element; that overlay is the session's job, not the layout's.
/// </para>
/// <para>
/// It reads the definition's model (helm-chart-disl-fbl, task 9): each node's type, its <c>path</c>, the
/// values layer's <c>isDefault</c>, a template's derived <c>role</c> and the name it is drawn with, in the
/// model's order, which is the plugin's. The definition names a layout as a plugin (finding A15), so the
/// bands are code.
/// </para>
/// </remarks>
public static class HelmLayout
{
    private const double Left = 40;
    private const double Top = 40;
    private const double ColumnGap = 60;
    private const double RowGap = 24;

    /// <summary>
    /// Every box the chart currently has: computed positions first, stored ones winning
    /// element by element, sizes staying measured - a stale stored id simply has nothing to
    /// override (core's own overlay semantics). The session's every render and the resolver's
    /// payload both come through here, so a selection can never place an element elsewhere
    /// than the canvas drew it.
    /// </summary>
    public static IReadOnlyDictionary<string, Rect> Arranged(
        DislDiagram diagram, IReadOnlyDictionary<string, RegistrationPosition> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var boxes = Compute(diagram);
        if (stored.Count == 0)
        {
            return boxes;
        }

        var overlaid = new Dictionary<string, Rect>(boxes.Count, StringComparer.Ordinal);
        foreach ((string id, Rect box) in boxes)
        {
            overlaid[id] = stored.TryGetValue(id, out var position)
                ? box with { X = position.X, Y = position.Y }
                : box;
        }

        return overlaid;
    }

    public static IReadOnlyDictionary<string, Rect> Compute(DislDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);

        var boxes = new Dictionary<string, Rect>(StringComparer.Ordinal);
        var nodes = diagram.Nodes;

        var metadata = nodes
            .Where(node => node.Type.Name is "Chart" or "Lock" or "Schema" or "Crds")
            .OrderBy(Rank)
            .ToArray();
        var values = nodes
            .Where(node => node.Type.Name == "Values")
            .OrderBy(node => node.ValueOf("isDefault") is true ? 0 : 1)
            .ThenBy(HelmElementMapper.PathOf, StringComparer.Ordinal)
            .ToArray();
        var templates = nodes
            .Where(node => node.IsA("Template"))
            .OrderBy(RoleRank)
            .ThenBy(HelmElementMapper.PathOf, StringComparer.Ordinal)
            .ToArray();
        var dependencies = nodes
            .Where(node => node.Type.Name == "Dependency")
            .OrderBy(HelmElementMapper.NameOf, StringComparer.Ordinal)
            .ToArray();
        var vendored = nodes
            .Where(node => node.IsA("Vendored"))
            .OrderBy(HelmElementMapper.PathOf, StringComparer.Ordinal)
            .ToArray();

        var x = Left;
        x = Column(boxes, metadata, x);
        x = Column(boxes, values, x);
        x = Column(boxes, templates, x);
        x = Column(boxes, dependencies, x);
        Column(boxes, vendored, x);

        return boxes;
    }

    private static double Column(Dictionary<string, Rect> boxes, IReadOnlyList<DislElement> nodes, double x)
    {
        var y = Top;
        double widest = 0;
        foreach (var node in nodes)
        {
            (double width, double height) = SizeOf(node);
            boxes[node.Id] = new Rect(x, y, width, height);
            y += height + RowGap;
            widest = Math.Max(widest, width);
        }

        // An empty column takes no room, so a chart without crds or vendored content does not
        // carry a hole where they would have been.
        return widest > 0 ? x + widest + ColumnGap : x;
    }

    private static (double Width, double Height) SizeOf(DislElement node) => node.Type.Name switch
    {
        "Chart" => (220, 110),
        "Values" => (200, 72),
        "Schema" => (200, 48),
        "Template" or "Partial" => (240, 60),
        "Crds" => (200, 48),
        "Dependency" => (210, 96),
        "Subchart" => (210, 84),
        "Archive" => (210, 52),
        "Lock" => (200, 56),
        _ => (200, 60),
    };

    private static int Rank(DislElement node) => node.Type.Name switch
    {
        "Chart" => 0,
        "Lock" => 1,
        "Schema" => 2,
        "Crds" => 3,
        _ => 4,
    };

    /// <summary>Manifests first, then partials, notes and test hooks, by the template's derived <c>role</c>.</summary>
    private static int RoleRank(DislElement node) => node.ValueOf("role") switch
    {
        "manifest" => 0,
        "partial" => 1,
        "notes" => 2,
        "test" => 3,
        _ => 4,
    };
}
