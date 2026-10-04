using Google.Protobuf;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>A <c>.skv</c> model as the library's elements: its nodes as bars and its flows as bands, laid out.</summary>
/// <remarks>
/// <para>
/// <b>Nodes come first</b>, so a flow never arrives before the ends it is drawn between.
/// </para>
/// <para>
/// <b>What a viewport admits</b> follows the convention every module shares: the layout is computed
/// over the whole document and only then culled; a node is kept when its bar overlaps the view, or
/// when a flow from a kept node reaches it, so no flow arrives with one end missing; and a flow is
/// kept structurally, on whether both of its ends were kept, never on a position of its own - it
/// has none.
/// </para>
/// </remarks>
public sealed class SankeyElementMapper
{
    private const string Prefix = "etalii/sankey+";

    /// <summary>The library type of a node.</summary>
    public const string NodeType = Prefix + "node";

    /// <summary>The library type of a flow.</summary>
    public const string FlowType = Prefix + "flow";

    /// <summary>Where a node's labels sit when it is in the first column: before the bar.</summary>
    public const string LeftSide = "left";

    /// <summary>Where every other node's labels sit: after the bar.</summary>
    public const string RightSide = "right";

    /// <summary>Everything drawable, unculled - what a selection is looked up in.</summary>
    public IReadOnlyList<DiagramElement> All(SankeyModel model) => Visible(model, DiagramViewport.Unbounded);

    /// <summary>The elements a view of <paramref name="viewport"/> holds.</summary>
    public IReadOnlyList<DiagramElement> Visible(SankeyModel model, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(model);

        var layout = SankeyLayout.Of(model);

        var kept = layout.Nodes
            .Where(node => Overlaps(layout.Boxes[node.Id], viewport))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        // A visible node pulls in what it is joined to, so every flow touching it arrives whole.
        foreach (var flow in layout.Flows.Where(flow => kept.Contains(flow.From) || kept.Contains(flow.To)).ToList())
        {
            kept.Add(flow.From);
            kept.Add(flow.To);
        }

        var byId = layout.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        return
        [
            .. layout.Nodes.Where(node => kept.Contains(node.Id)).Select(node => Node(model, layout, node)),
            .. layout.Flows.Where(flow => kept.Contains(flow.From) && kept.Contains(flow.To)).Select(flow => Flow(model, layout, flow, byId)),
        ];
    }

    private static bool Overlaps(SankeyBox box, DiagramViewport viewport) =>
        box.Right >= viewport.MinX && box.X <= viewport.MaxX && box.Bottom >= viewport.MinY && box.Y <= viewport.MaxY;

    private static DiagramElement Node(SankeyModel model, SankeyLayout layout, SankeyNode node)
    {
        // The library draws from the centre.
        var box = layout.Boxes[node.Id];
        var value = layout.Values[node.Id];
        (string word, string hex) = SankeyColors.Resolve(node.Color);
        var payload = new SankeyNodePayload
        {
            Name = node.Label,
            Value = value,
            DisplayValue = SankeyFormat.Write(node.Format.Length > 0 ? node.Format : model.Settings.Format, value),
            Note = node.Note,
            Color = word,
            CustomColor = hex,
            Width = box.Width,
            Height = box.Height,
            Column = layout.Columns[node.Id],
            Side = layout.Columns[node.Id] == 0 ? LeftSide : RightSide,
        };

        return Pack(node.Id, box.CentreX, box.CentreY, NodeType, payload);
    }

    private static DiagramElement Flow(SankeyModel model, SankeyLayout layout, SankeyFlow flow, Dictionary<string, SankeyNode> nodes)
    {
        var band = layout.Bands[flow.Id];
        var value = Math.Max(0, flow.Value ?? 0);

        // A flow states its own colour, or takes the colour of the end the document names.
        var end = nodes[model.Settings.FlowColor == SankeySettings.FromSource ? flow.From : flow.To];
        (string endWord, string endHex) = SankeyColors.Resolve(end.Color);
        (string word, string hex) = SankeyColors.Resolve(flow.Color, endWord.Length > 0 ? endWord : endHex);

        var payload = new SankeyFlowPayload
        {
            FromElementId = flow.From,
            ToElementId = flow.To,
            Value = value,
            DisplayValue = SankeyFormat.Write(model.Settings.Format, value),
            Thickness = band.Thickness,
            SourceAt = band.SourceAt,
            TargetAt = band.TargetAt,
            Color = word,
            CustomColor = hex,
            Backward = band.Backward,
        };

        // A flow has no position of its own; it follows its ends.
        return Pack(flow.Id, 0d, 0d, FlowType, payload);
    }

    private static DiagramElement Pack(string id, double x, double y, string type, IMessage payload) =>
        new(id, x, y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
