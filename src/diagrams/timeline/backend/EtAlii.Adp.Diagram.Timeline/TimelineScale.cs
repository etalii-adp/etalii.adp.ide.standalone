using System.Globalization;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The only conversion between a time and a horizontal coordinate, in either direction.
/// </summary>
/// <remarks>
/// <para>
/// The module's x axis is <b>seconds since the Unix epoch, as a double</b>. That unit is chosen
/// so a drag can travel through the existing <c>MoveElementRequest.position</c> - two doubles -
/// without the backend ever learning the client's zoom: the canvas layers a pure seconds-to-pixels
/// view transform on top, and what crosses the wire is a time, not a pixel. Precision is not a
/// concern at this scale: epoch seconds are ~1.8e9 and a double's 53-bit mantissa leaves
/// sub-microsecond resolution against a format whose finest unit is a second.
/// </para>
/// <para>
/// This is the module's most error-prone surface, exactly as the axis flip is in the Wardley
/// module - so it lives here as one function pair with a round-trip property test, and nothing
/// else (not the parser, not a command, not the canvas) is permitted to do the arithmetic itself.
/// </para>
/// </remarks>
public static class TimelineScale
{
    /// <summary>The x for an instant: whole seconds since the epoch, fractional where the value has them.</summary>
    public static double ToSeconds(DateTimeOffset value) =>
        (value - DateTimeOffset.UnixEpoch).TotalSeconds;

    /// <summary>
    /// The instant for an x, rounded to the whole second - the format's finest unit - and carrying
    /// the precision it must be written back with.
    /// </summary>
    /// <remarks>
    /// A <see cref="TimelinePrecision.Date"/> value snaps to the start of its day, so a horizontal
    /// drag of a date-only element lands on a date rather than on a time nobody wrote. That is
    /// what keeps Requirement 3.2 - no mixed precision within one element - from being violated
    /// by a gesture: the element's own precision travels through the round trip and the drag
    /// gives back the same kind of value it was handed.
    /// </remarks>
    public static DateTimeOffset ToTime(double seconds, TimelinePrecision precision)
    {
        var value = DateTimeOffset.UnixEpoch.AddSeconds(Math.Round(seconds));
        return precision == TimelinePrecision.Date
            ? new DateTimeOffset(value.Year, value.Month, value.Day, 0, 0, 0, TimeSpan.Zero)
            : value;
    }

    /// <summary>
    /// The instant as document text, in the shape its precision dictates - never the other one.
    /// </summary>
    /// <remarks>
    /// The writer splices whatever this returns straight into the file, so this is where
    /// <c>2026-01-05</c> stays <c>2026-01-05</c> after a drag instead of becoming
    /// <c>2026-01-05T00:00:00</c> - the silent promotion the mixed-precision fixture exists to
    /// catch.
    /// </remarks>
    public static string ToText(DateTimeOffset value, TimelinePrecision precision) =>
        precision == TimelinePrecision.Date
            ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
}
