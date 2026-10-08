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
    /// What of <paramref name="model"/> falls inside <paramref name="viewport"/>, in the
    /// module's own units - seconds across, row-derived y down (view-delta-adoption
    /// Requirement 1.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two decisions here are this module's rather than the mechanism's, which is why they live
    /// with the mapper and not in anything shared (Requirement 4).
    /// </para>
    /// <para>
    /// <b>An element is a span, not a point.</b> Its packed <c>x</c> is where it begins, but a
    /// period runs to its end, and one that starts before the view and finishes inside it is
    /// exactly the element a reader is looking at. Culling on the begin instant alone would drop
    /// the long bars first - the ones a timeline exists to show.
    /// </para>
    /// <para>
    /// <b>A connection is visible when both its endpoints are.</b> Connections are packed at the
    /// origin deliberately: the canvas recomputes the curve from its endpoints' boxes, so their
    /// coordinates mean nothing and testing them against a viewport would be testing a
    /// placeholder. Sending a connection whose endpoint the connection does not hold would have
    /// the canvas draw a curve to an element that is not there.
    /// </para>
    /// <para>
    /// An element whose begin could not be read is packed at the epoch so it stays selectable
    /// while the panel names the problem; it is treated as a moment there rather than being
    /// hidden, so the same reasoning survives virtualization.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DiagramElement> Visible(TimelineModel model, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(model);

        var visible = new List<DiagramElement>();
        var shown = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in model.Elements)
        {
            if (!Intersects(element, viewport))
            {
                continue;
            }

            visible.Add(Element(element));
            shown.Add(element.Id);
        }

        foreach (var connection in model.Connections)
        {
            if (shown.Contains(connection.From) && shown.Contains(connection.To))
            {
                visible.Add(Connection(connection));
            }
        }

        return visible;
    }

    /// <summary>Whether an element's span and row meet the viewport.</summary>
    private static bool Intersects(TimelineElement element, DiagramViewport viewport)
    {
        var begin = element.Begin.IsReadable ? TimelineScale.ToSeconds(element.Begin.Value!.Value) : 0d;
        var end = element.End is { IsReadable: true, Value: not null } finish
            ? TimelineScale.ToSeconds(finish.Value.Value)
            : begin;

        // A row occupies its full height, so an element half-scrolled off the top is still in view.
        var top = TimelineRows.ToY(element.Row);
        var bottom = top + TimelineRows.Height;

        return end >= viewport.MinX
            && begin <= viewport.MaxX
            && bottom >= viewport.MinY
            && top <= viewport.MaxY;
    }

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
        if (element is { IsPeriod: true, End.IsReadable: true, Begin.IsReadable: true })
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

    private static DiagramElement Element(TimelineElement element)
    {
        var payload = new TimelineElementPayload
        {
            Begin = element.Begin.Text,
            End = element.End?.Text ?? "",
            Row = element.Row,
            DateOnly = element.Begin.Precision == TimelinePrecision.Date,
            Label = element.Label,
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
