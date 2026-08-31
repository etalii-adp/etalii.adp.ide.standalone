using EtAlii.Adp.Backend.Diagrams;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Turns a parsed <see cref="TimelineModel"/> into the core element and delta vocabulary, and a
/// received position back into a placement.
/// </summary>
/// <remarks>
/// <para>
/// Every coordinate on the wire is <c>TimelineScale</c>'s and <c>TimelineRows</c>' work - this
/// class does none of the arithmetic itself, which is the discipline the design carries over
/// from the Wardley module's axis flip. What crosses to core is an <see cref="DiagramElement"/>
/// whose x is seconds and whose y is <c>row × height</c>; the canvas layers zoom on top and the
/// backend never learns it.
/// </para>
/// <para>
/// A connection naming an element that does not exist still goes out, carrying the ids as
/// written, so the canvas can mark the dangling end and the validator can report it - a policy
/// borrowed deliberately from the Wardley mapper.
/// </para>
/// </remarks>
public sealed class TimelineElementMapper
{
    /// <summary>The mime-style kind of an element that occupies a period.</summary>
    public const string PeriodType = "generic/timeline+period";

    /// <summary>The mime-style kind of an element that marks a moment.</summary>
    public const string MomentType = "generic/timeline+moment";

    /// <summary>The mime-style kind of a connection.</summary>
    public const string ConnectionType = "generic/timeline+connection";

    /// <summary>
    /// Every element and connection of <paramref name="model"/>, positioned in the module's own
    /// coordinate space - the baseline a connection opens with.
    /// </summary>
    public IReadOnlyList<DiagramElement> Elements(TimelineModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var elements = new List<DiagramElement>();
        foreach (var element in model.Elements)
        {
            elements.Add(Element(element));
        }

        foreach (var connection in model.Connections)
        {
            elements.Add(Connection(connection));
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
    /// A received canvas position as the placement it means: a begin (and, for a period, the end
    /// that preserves its duration), and the row the y snaps to.
    /// </summary>
    /// <remarks>
    /// This is the whole of a drag's arithmetic, and none of it is new: the duration is carried
    /// over unchanged so a horizontal drag reschedules without resizing (Requirement 6.1), and
    /// the element's own precision travels through <see cref="TimelineScale.ToTime"/> so a
    /// date-only element lands on a date (Requirement 3.2).
    /// </remarks>
    public static (string Begin, string? End, int Row) Placement(TimelineElement element, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(element);

        var precision = element.Begin.Precision;
        var begin = TimelineScale.ToTime(x, precision);

        string? end = null;
        if (element is { IsPeriod: true, End.IsReadable: true } && element.Begin.IsReadable)
        {
            var duration = element.End!.Value!.Value - element.Begin.Value!.Value;
            end = TimelineScale.ToText(begin + duration, element.End.Precision);
        }
        else if (element.End is not null)
        {
            // A period whose times cannot be read cannot preserve a duration nobody can compute;
            // the end stays exactly as written and only the begin moves.
            end = element.End.Text;
        }

        return (TimelineScale.ToText(begin, precision), end, TimelineRows.ToNearestRow(y));
    }

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

    private static DiagramElement Element(TimelineElement element)
    {
        var payload = new TimelineElementPayload
        {
            Begin = element.Begin.Text,
            End = element.End?.Text ?? "",
            Row = element.Row,
            DateOnly = element.Begin.Precision == TimelinePrecision.Date,
        };

        // x is the begin when it is readable; an unreadable one lands at the epoch rather than
        // nowhere, so the element stays visible and selectable while the panel names the problem
        // (Requirement 12.2).
        var x = element.Begin.IsReadable ? TimelineScale.ToSeconds(element.Begin.Value!.Value) : 0d;

        return Pack(
            element.Id,
            x,
            TimelineRows.ToY(element.Row),
            element.IsPeriod ? PeriodType : MomentType,
            payload);
    }

    private static DiagramElement Connection(TimelineConnection connection)
    {
        var payload = new TimelineConnectionPayload
        {
            FromElementId = connection.From,
            ToElementId = connection.To,
            Label = connection.Label,
        };

        // A connection has no position of its own: the canvas recomputes the curve from its
        // endpoints' boxes on every change (Requirement 8.6), so the coordinates here are
        // deliberately meaningless.
        return Pack(connection.Id, 0d, 0d, ConnectionType, payload);
    }

    private static DiagramElement Pack(string id, double x, double y, string type, IMessage payload) =>
        new(id, x, y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
