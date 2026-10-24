using System.Runtime.CompilerServices;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// The CEL functions of the definition's two plugins (DISL §13.1.1): the five of
/// <c>net.etalii.adp.etalii.sankeyLayout</c>, answered from the module's own <see cref="SankeyLayout"/>, and
/// <c>writtenEnd</c> of <c>net.etalii.adp.etalii.sankeyEnds</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>One layout per diagram, never one per call.</b> The layout is computed the first time a function asks
/// for it and kept with the <see cref="DislDiagram"/> it was computed for, so a form or a notation that calls
/// <c>sankeyColumn</c> for every node reads one layout. A model is built afresh for every reading, so keying on
/// the instance means an edit recomputes it and nothing else does.
/// </para>
/// <para>
/// <b>The layout is the module's</b>: it is computed from what the diagram holds, read back into the
/// <see cref="SankeyModel"/> the layout has always taken, so the canvas and the definition draw the same
/// columns, bars and bands. A node is known to the layout by the id it is written with, the binding's
/// <c>storedId</c>, and a flow by its model id, which is its stated id or its ends as written.
/// </para>
/// <para>
/// <b>An end as written</b> (<c>writtenEnd</c>) is the <c>from</c> or <c>to</c> text a flow was read with,
/// which the model keeps as <see cref="DislElement.SourceId"/> and <see cref="DislElement.TargetId"/>, because
/// an end that names no node is null in the model and CEL could not otherwise say what it named.
/// </para>
/// </remarks>
internal static class SankeyFunctions
{
    private static readonly ConditionalWeakTable<DislDiagram, SankeyLayout> _layouts = new();

    /// <summary>The implementations, for <see cref="BundledDefinition.Load(System.Reflection.Assembly, string, DislPluginFunctions)"/>.</summary>
    public static DislPluginFunctions Plugins() => new DislPluginFunctions()
        .Add("sankeyColumn", arguments => (long)Drawn<int>(arguments[0], "sankeyColumn", (layout, id) => layout.Columns.TryGetValue(id, out var column) ? column : null))
        .Add("sankeyColumnCount", (_, used) => (long)LayoutOf(used).ColumnOrder.Count)
        .Add("sankeyScale", (_, used) => LayoutOf(used).Scale)
        .Add("sankeyTop", arguments => Drawn<double>(arguments[0], "sankeyTop", (layout, id) => layout.Boxes.TryGetValue(id, out var box) ? box.Y : null))
        .Add("bandAt", arguments => BandAt(arguments[0], arguments[1]))
        .Add("writtenEnd", arguments => WrittenEnd(arguments[0], arguments[1]));

    /// <summary>The layout of <paramref name="diagram"/>, computed once for it.</summary>
    public static SankeyLayout LayoutOf(DislDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return _layouts.GetValue(diagram, drawn => SankeyLayout.Of(ModelOf(drawn)));
    }

    /// <summary>The id <paramref name="node"/> is written with, the binding's <c>storedId</c>: empty for a node without one.</summary>
    private static string WrittenId(DislElement node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.HostAttributes.TryGetValue("storedId", out var stored) && stored is string id ? id : "";
    }

    private static SankeyLayout LayoutOf(IReadOnlyDictionary<string, object?> used) =>
        used.TryGetValue("diagram", out var diagram) && diagram is DislDiagram drawn
            ? LayoutOf(drawn)
            : throw new CelException("The Sankey layout is computed for a diagram, which this call has none of.");

    private static T Drawn<T>(object? argument, string function, Func<SankeyLayout, string, T?> read)
        where T : struct
    {
        if (argument is not DislElement { Type.IsRelation: false } node || !node.IsA("Node"))
        {
            throw new CelException($"{function}() takes a node of this diagram.");
        }

        return read(LayoutOf(node.Diagram), WrittenId(node)) is { } value && node.Id == WrittenId(node)
            ? value
            : throw new CelException($"{function}() takes a drawn node, and '{node.Id}' is not drawn.");
    }

    private static double BandAt(object? flow, object? end)
    {
        if (flow is not DislElement { Type.IsRelation: true } relation || end is not ("source" or "target"))
        {
            throw new CelException("bandAt() takes a flow of this diagram and 'source' or 'target'.");
        }

        return LayoutOf(relation.Diagram).Bands.TryGetValue(relation.Id, out var band)
            ? end is "source" ? band.SourceAt : band.TargetAt
            : throw new CelException($"bandAt() takes a drawn flow, and '{relation.Id}' is not drawn.");
    }

    private static string WrittenEnd(object? relation, object? end) =>
        relation is DislElement { Type.IsRelation: true } element && end is "source" or "target"
            ? (end is "source" ? element.SourceId : element.TargetId) ?? ""
            : throw new CelException("writtenEnd() takes a flow of this diagram and 'source' or 'target'.");

    /// <summary>
    /// What <paramref name="diagram"/> holds as the model the layout reads: every node in order by the id it is
    /// written with, every flow by its stated id and its ends as written, and the thickness. The layout itself
    /// decides what is drawn, as it always has: the first holder of an id, and a flow between two such nodes.
    /// </summary>
    private static SankeyModel ModelOf(DislDiagram diagram)
    {
        var nodes = diagram.NodesOfType("Node")
            .Select(node => new SankeyNode(
                WrittenId(node),
                Text(node, "name"),
                Text(node, "color"),
                Text(node, "note"),
                Text(node, "format"),
                node.Attributes.TryGetValue("column", out var column) && column is long whole ? (int)whole : null,
                Text(node, "description"),
                default))
            .ToList();
        var flows = diagram.RelationsOfType("Flow")
            .Select(flow => new SankeyFlow(
                Text(flow, "statedId"),
                flow.SourceId ?? "",
                flow.TargetId ?? "",
                Number(flow, "quantity"),
                Number(flow, "step"),
                Text(flow, "color"),
                Text(flow, "description"),
                default))
            .ToList();
        var thickness = diagram.Attributes.TryGetValue("thickness", out var stated) && stated is double value && double.IsFinite(value) && value > 0
            ? Math.Clamp(value, SankeyGeometry.MinimumScale, SankeyGeometry.MaximumScale)
            : SankeySettings.Default.Thickness;
        return new SankeyModel(SankeySettings.Default with { Thickness = thickness }, nodes, flows, [], SankeyModel.CurrentVersion);
    }

    private static string Text(DislElement element, string attribute) =>
        element.Attributes.TryGetValue(attribute, out var value) && value is string text ? text : "";

    private static double? Number(DislElement element, string attribute) =>
        element.Attributes.TryGetValue(attribute, out var value) && value is double number ? number : null;
}
