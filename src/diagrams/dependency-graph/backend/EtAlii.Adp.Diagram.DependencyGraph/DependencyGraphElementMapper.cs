using EtAlii.Adp.Backend.Diagrams;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Turns a parsed <see cref="DependencyGraphModel"/> into the core element and delta vocabulary,
/// and a received position back into a placement.
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
    public IReadOnlyList<DiagramElement> Elements(DependencyGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var elements = new List<DiagramElement>();
        foreach (var element in model.Elements)
        {
            elements.Add(Element(element));
        }

        foreach (var relation in model.Relations)
        {
            elements.Add(Relation(relation));
        }

        return elements;
    }

    /// <summary>
    /// The difference between two renderings, as adds and removes. An edit is an add carrying
    /// the element in its new state, which is what makes the contract's four actions enough.
    /// </summary>
    public IReadOnlyList<DiagramDelta> Diff(
        IReadOnlyList<DiagramElement> before,
        IReadOnlyList<DiagramElement> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var previous = before.ToDictionary(element => element.Id, StringComparer.Ordinal);
        var deltas = new List<DiagramDelta>();

        var changed = after
            .Where(element => !previous.TryGetValue(element.Id, out var was) || !Same(was, element))
            .ToArray();

        if (changed.Length > 0)
        {
            deltas.Add(new DiagramAddDelta(changed));
        }

        var current = after.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        var gone = before.Select(element => element.Id).Where(id => !current.Contains(id)).ToArray();
        if (gone.Length > 0)
        {
            deltas.Add(new DiagramRemoveDelta(gone));
        }

        return deltas;
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

    /// <summary>
    /// Whether two renderings of one element say the same thing. Not the record's own equality:
    /// <see cref="ReadOnlyMemory{T}"/> compares its reference rather than its bytes, so two
    /// identical payloads serialized into different arrays would count as a change and every
    /// re-render would re-deliver the whole diagram.
    /// </summary>
    private static bool Same(DiagramElement left, DiagramElement right) =>
        left.X.Equals(right.X)
        && left.Y.Equals(right.Y)
        && left.Type == right.Type
        && left.Payload.Span.SequenceEqual(right.Payload.Span);

    private static DiagramElement Element(DependencyGraphElement element)
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

    private static DiagramElement Relation(DependencyGraphRelation relation)
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

    private static DiagramElement Pack(string id, double x, double y, string type, IMessage payload) =>
        new(id, x, y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
