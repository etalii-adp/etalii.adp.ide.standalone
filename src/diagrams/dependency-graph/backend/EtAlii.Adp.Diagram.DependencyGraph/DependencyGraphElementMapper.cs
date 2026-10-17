using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Layout;
using EtAlii.Adp.Specification.Disl;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Turns the DISL model of a <see cref="DependencyGraphBody"/> into the core element and delta
/// vocabulary, and a received position back into a placement.
/// </summary>
/// <remarks>
/// <para>
/// The timeline projected a time onto x through a scale; this reads the authored <c>x</c>
/// directly, because there is nothing to project. <c>DependencyGraphRows</c> still owns y. What
/// crosses to core is a <see cref="DiagramElement"/> whose x is the number in the file and whose
/// y is <c>row × height</c>; the canvas layers zoom on top and the backend never learns it.
/// </para>
/// <para>
/// A relation naming a node that does not exist still goes out, carrying the ids as written, so
/// the canvas can mark the dangling end and the validator can report it - a policy borrowed
/// deliberately from the Wardley mapper.
/// </para>
/// <para>
/// Every id sent is the id as written - the binding's <c>storedId</c>, and a dependency's ends its
/// <c>writtenFrom</c> and <c>writtenTo</c> - so the client knows an element by what the file says,
/// a dangling end and a repeated or missing id included.
/// </para>
/// <para>
/// The relation's payload names its ends <em>in order</em>: <c>from</c> depends on <c>to</c>, and
/// the <c>to</c> end is where the canvas puts the arrowhead. That ordering is the only thing
/// carrying this type's meaning across the wire, so nothing on this path may normalise it.
/// </para>
/// </remarks>
public sealed class DependencyGraphElementMapper
{
    /// <summary>The mime-style kind of a node.</summary>
    public const string NodeType = "generic/dependencies+node";

    /// <summary>The mime-style kind of a directed depends-on edge.</summary>
    public const string RelationType = "generic/dependencies+relation";

    /// <summary>
    /// Every node and relation of <paramref name="model"/>, positioned in the module's own
    /// coordinate space - the baseline a connection opens with.
    /// </summary>
    public IReadOnlyList<DiagramElement> Elements(DislModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        (List<Node> nodes, List<Dependency> relations) = Read(model.Diagram);

        var elements = new List<DiagramElement>();
        foreach (var element in nodes)
        {
            elements.Add(Element(element));
        }

        foreach (var relation in relations)
        {
            elements.Add(Relation(relation));
        }

        return elements;
    }

    /// <summary>The drawn width of a node, in the module's own x units - what the canvas gives it.</summary>
    private const double NodeWidth = 160d;

    /// <summary>The drawn height of a node, in the module's own y units.</summary>
    private const double NodeHeight = 36d;

    /// <summary>
    /// The elements of <paramref name="model"/> a viewport can see: every node whose box it
    /// intersects, and every relation both of whose ends are among them
    /// (view-delta-adoption Requirement 1.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A relation follows its endpoints and has no box of its own, so it is delivered exactly
    /// when both ends are - which is also what the canvas already does with one, drawing nothing
    /// for a curve whose endpoint it does not hold.
    /// </para>
    /// <para>
    /// The rectangle arrives in this module's own units, x as authored and y as
    /// <c>row × <see cref="DependencyGraphRows.Height"/></c>; nothing converts it on the way in,
    /// because the units are this module's business (Requirement 3.4).
    /// </para>
    /// </remarks>
    public IReadOnlyList<DiagramElement> Visible(DislModel model, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(model);
        (List<Node> nodes, List<Dependency> relations) = Read(model.Diagram);

        var byId = nodes.ToDictionary(element => element.Id, StringComparer.Ordinal);
        var shownIds = nodes
            .Where(element => Intersects(element, viewport))
            .Select(element => element.Id)
            .ToHashSet(StringComparer.Ordinal);

        // A relation is drawn where its SPAN - the hull of its two end boxes - touches the
        // viewport. Requiring both ends in view instead hid, on zooming in, every line whose
        // far end left the window (found in the field). Its ends ride along as anchors, so
        // the canvas always has two boxes to draw the line between.
        var drawnRelations = relations
            .Where(relation => byId.TryGetValue(relation.From, out var from)
                && byId.TryGetValue(relation.To, out var to)
                && SpanIntersects(from, to, viewport))
            .ToArray();
        foreach (var relation in drawnRelations)
        {
            shownIds.Add(relation.From);
            shownIds.Add(relation.To);
        }

        // Document order is preserved on both passes, so one viewport always yields one sequence.
        return
        [
            .. nodes.Where(element => shownIds.Contains(element.Id)).Select(Element),
            .. drawnRelations.Select(Relation),
        ];
    }

    /// <summary>
    /// The nodes and dependencies of a DISL model, in its order, each by what is written: its
    /// stored id, its label, its place, and a dependency's ends as written.
    /// </summary>
    private static (List<Node> Nodes, List<Dependency> Relations) Read(DislDiagram diagram)
    {
        List<Node> nodes =
        [
            .. diagram.NodesOfType("Node").Select(node => new Node(
                DependencyGraphDefinition.WrittenId(node),
                node.ValueOf("label") as string ?? "",
                node.ValueOf("x") is double x ? x : 0d,
                node.ValueOf("row") is long row ? checked((int)row) : 0)),
        ];
        List<Dependency> relations =
        [
            .. diagram.RelationsOfType("DependsOn").Select(relation => new Dependency(
                DependencyGraphDefinition.WrittenId(relation),
                DependencyGraphDefinition.HostText(relation, "writtenFrom"),
                DependencyGraphDefinition.HostText(relation, "writtenTo"),
                relation.ValueOf("label") as string ?? "")),
        ];
        return (nodes, relations);
    }

    /// <summary>
    /// A received canvas position as the placement it means: the horizontal coordinate as sent,
    /// and the row the y snaps to.
    /// </summary>
    /// <remarks>
    /// The whole of a drag's arithmetic, and there is almost none of it left. The timeline had to
    /// convert an x into a begin, carry a duration across so a horizontal drag rescheduled without
    /// resizing, and preserve the element's precision on the way back. A node has no duration and
    /// no precision: its x is its x.
    /// </remarks>
    public static (double X, int Row) Placement(double x, double y) =>
        (x, DependencyGraphRows.ToNearestRow(y));

    /// <summary>Whether a node's box overlaps the viewport at all - touching edges count as seen.</summary>
    private static bool Intersects(Node element, DiagramViewport viewport)
    {
        var top = DependencyGraphRows.ToY(element.Row);
        return viewport.Intersects(new Rect(element.X, top, NodeWidth, NodeHeight));
    }

    /// <summary>
    /// Whether the hull of two nodes' boxes - a relation's span - touches the viewport. The
    /// drawn line bows a little for the bezier; the criterion is the bounding box, not its
    /// every pixel.
    /// </summary>
    private static bool SpanIntersects(Node from, Node to, DiagramViewport viewport)
    {
        var fromTop = DependencyGraphRows.ToY(from.Row);
        var toTop = DependencyGraphRows.ToY(to.Row);
        var left = Math.Min(from.X, to.X);
        var top = Math.Min(fromTop, toTop);
        return viewport.Intersects(new Rect(left, top, Math.Abs(from.X - to.X) + NodeWidth, Math.Abs(fromTop - toTop) + NodeHeight));
    }

    private static DiagramElement Element(Node element)
    {
        var payload = new DependencyGraphElementPayload
        {
            X = element.X,
            Row = element.Row,
            Label = element.Label,
        };

        return Pack(
            element.Id,
            element.X,
            DependencyGraphRows.ToY(element.Row),
            NodeType,
            payload);
    }

    private static DiagramElement Relation(Dependency relation)
    {
        var payload = new DependencyGraphRelationPayload
        {
            FromElementId = relation.From,
            ToElementId = relation.To,
            Label = relation.Label,
        };

        // A relation has no position of its own: the canvas recomputes the curve from its
        // endpoints' boxes on every change, so the coordinates here are deliberately meaningless.
        // Its direction is not - that travels in the payload, and the arrowhead is drawn at the
        // `to` end.
        return Pack(relation.Id, 0d, 0d, RelationType, payload);
    }

    /// <summary>A node as it is drawn: the id it is written with, its label and its place.</summary>
    private sealed record Node(string Id, string Label, double X, int Row);

    /// <summary>A dependency as it is drawn: the id it is written with, its ends as written, and its label.</summary>
    private sealed record Dependency(string Id, string From, string To, string Label);

    private static DiagramElement Pack(string id, double x, double y, string type, IMessage payload) =>
        new(id, x, y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
