using Google.Protobuf;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// A <c>.supply</c> model as the library's elements: groups, the seven stages and flows, laid out
/// and marked with the trace through the current selection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Groups come first</b>, so the canvas draws their frames beneath the nodes inside them.
/// </para>
/// <para>
/// <b>What a viewport admits</b> follows the convention every module shares: the layout is computed
/// over the whole document and only then culled; a group frame is kept whenever its rectangle
/// overlaps the view; a node is kept when its box does, or when a flow from a kept node reaches it,
/// so no flow arrives with one end missing; and a flow is kept structurally, on whether both of its
/// ends were kept, never on a position of its own - it has none.
/// </para>
/// <para>
/// <b>A flow's weight is relative to the heaviest flow in the same unit</b>, so a chain that moves
/// tonnes of ore and millions of cars draws both at a width that means something.
/// </para>
/// </remarks>
public sealed class SupplyChainElementMapper
{
    private const string Prefix = "etalii/supply-chain+";

    /// <summary>The library type of a group's frame.</summary>
    public const string GroupType = Prefix + "group";

    /// <summary>The library type of a source.</summary>
    public const string SourceType = Prefix + SupplyChainNodeTypes.Source;

    /// <summary>The library type of a processor.</summary>
    public const string ProcessorType = Prefix + SupplyChainNodeTypes.Processor;

    /// <summary>The library type of a producer.</summary>
    public const string ProducerType = Prefix + SupplyChainNodeTypes.Producer;

    /// <summary>The library type of an integrator.</summary>
    public const string IntegratorType = Prefix + SupplyChainNodeTypes.Integrator;

    /// <summary>The library type of a hub.</summary>
    public const string HubType = Prefix + SupplyChainNodeTypes.Hub;

    /// <summary>The library type of an outlet.</summary>
    public const string OutletType = Prefix + SupplyChainNodeTypes.Outlet;

    /// <summary>The library type of a consumer.</summary>
    public const string ConsumerType = Prefix + SupplyChainNodeTypes.Consumer;

    /// <summary>The library type of a flow.</summary>
    public const string FlowType = Prefix + "flow";

    /// <summary>The library type a stage is drawn as.</summary>
    public static string NodeTypeOf(string stage) => Prefix + stage;

    /// <summary>Everything drawable, unculled, with no trace - what a selection is looked up in.</summary>
    public IReadOnlyList<DiagramElement> All(SupplyChainModel model) =>
        Visible(model, DiagramViewport.Unbounded, SupplyChainTrace.None);

    /// <summary>The elements a view of <paramref name="viewport"/> holds, marked with <paramref name="trace"/>.</summary>
    public IReadOnlyList<DiagramElement> Visible(SupplyChainModel model, DiagramViewport viewport, SupplyChainTrace trace)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(trace);

        var layout = SupplyChainLayout.Of(model);

        var kept = layout.Nodes
            .Where(node => Overlaps(layout.NodeBoxes[node.Id], viewport))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        // A visible node pulls in what it trades with, so every flow touching it arrives whole.
        foreach (var flow in layout.Flows.Where(flow => kept.Contains(flow.From) || kept.Contains(flow.To)).ToList())
        {
            kept.Add(flow.From);
            kept.Add(flow.To);
        }

        // A band's width compares like with like: tonnes of ore against tonnes, cars against cars.
        var heaviest = layout.Flows
            .GroupBy(flow => flow.Unit, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(flow => flow.Volume ?? 0), StringComparer.Ordinal);

        // A card's bar compares like with like too, and over the whole document rather than the view.
        var largest = layout.Nodes
            .Where(node => node.Quantity is not null)
            .GroupBy(node => node.Unit, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(node => node.Quantity ?? 0), StringComparer.Ordinal);

        return
        [
            .. layout.Groups
                .Where(group => Overlaps(layout.GroupBoxes[group.Id], viewport))
                .Select(group => Group(layout, group, trace)),
            .. layout.Nodes.Where(node => kept.Contains(node.Id)).Select(node => Node(layout, node, largest, trace)),
            .. layout.Flows.Where(flow => kept.Contains(flow.From) && kept.Contains(flow.To)).Select(flow => Flow(flow, heaviest[flow.Unit], layout.Lanes.GetValueOrDefault(flow.Id), trace)),
        ];
    }

    private static bool Overlaps(SupplyChainBox box, DiagramViewport viewport) =>
        box.Right >= viewport.MinX && box.X <= viewport.MaxX && box.Bottom >= viewport.MinY && box.Y <= viewport.MaxY;

    private static DiagramElement Group(SupplyChainLayout layout, SupplyChainGroup group, SupplyChainTrace trace)
    {
        var box = layout.GroupBoxes[group.Id];
        var payload = new SupplyChainGroupPayload
        {
            Name = group.Name.Length > 0 ? group.Name : group.Id,
            Width = box.Width,
            Height = box.Height,
            Members = layout.Nodes.Count(node => node.Group == group.Id),
            Trace = trace.Of(group.Id),
        };

        return Pack(group.Id, box.CentreX, box.CentreY, GroupType, payload);
    }

    private static DiagramElement Node(SupplyChainLayout layout, SupplyChainNode node, Dictionary<string, double> largest, SupplyChainTrace trace)
    {
        // The document holds the top-left; the library draws from the centre.
        var box = layout.NodeBoxes[node.Id];
        var payload = new SupplyChainNodePayload
        {
            Name = node.Name.Length > 0 ? node.Name : node.Id,
            GroupId = layout.GroupBoxes.ContainsKey(node.Group) ? node.Group : "",
            Quantity = node.Quantity ?? 0,
            HasQuantity = node.Quantity is not null,
            Unit = node.Unit,
            Width = box.Width,
            Height = box.Height,
            Trace = trace.Of(node.Id),
            Stage = SupplyChainNodeTypes.Display(node.Type),
            Share = node.Quantity is { } quantity && largest.TryGetValue(node.Unit, out var most) && most > 0 ? Math.Clamp(quantity / most, 0, 1) : 0,
        };

        return Pack(node.Id, box.CentreX, box.CentreY, NodeTypeOf(node.Type), payload);
    }

    private static DiagramElement Flow(SupplyChainFlow flow, double heaviest, IReadOnlyList<(double X, double Y)>? lanes, SupplyChainTrace trace)
    {
        var payload = new SupplyChainFlowPayload
        {
            FromElementId = flow.From,
            ToElementId = flow.To,
            Product = flow.Product,
            Volume = flow.Volume ?? 0,
            HasVolume = flow.Volume is not null,
            Unit = flow.Unit,
            Weight = heaviest > 0 && flow.Volume is { } volume ? Math.Clamp(volume / heaviest, 0, 1) : 0,
            Trace = trace.Of(flow.Id),
        };
        payload.Lanes.AddRange((lanes ?? []).Select(lane => new SupplyChainLanePoint { X = lane.X, Y = lane.Y }));

        // A flow has no position of its own; it follows its ends.
        return Pack(flow.Id, 0d, 0d, FlowType, payload);
    }

    private static DiagramElement Pack(string id, double x, double y, string type, IMessage payload) =>
        new(id, x, y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
