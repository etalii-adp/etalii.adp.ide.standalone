namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The only conversion between a row index and a vertical coordinate, and the nearest-row inverse
/// a drag snaps through.
/// </summary>
/// <remarks>
/// A row is an integer the author owns - negative is as valid as positive, and gaps are the
/// author's business (Requirement 3.5). The height is this module's rendering constant, not data:
/// it appears in the payload's y only as <c>row × Height</c>, and the client divides it straight
/// back out, so changing it re-spaces every diagram without touching a single file.
/// </remarks>
public static class TimelineRows
{
    /// <summary>The vertical distance between adjacent rows, in the module's own y units.</summary>
    public const double Height = 60d;

    /// <summary>The y for a row.</summary>
    public static double ToY(int row) => row * Height;

    /// <summary>
    /// The row nearest a y - what a vertical drag snaps to on release (Requirement 6.2).
    /// </summary>
    /// <remarks>
    /// <see cref="Math.Round(double, MidpointRounding)"/> with
    /// <see cref="MidpointRounding.AwayFromZero"/> rather than the default banker's rounding:
    /// the exact midpoint between rows 0 and 1 must resolve the same way as the midpoint between
    /// rows 1 and 2, or dragging an element to the boundary snaps up on odd rows and down on even
    /// ones - an off-by-one a user would perceive as flaky snapping rather than as a bug they
    /// could report.
    /// </remarks>
    public static int ToNearestRow(double y) =>
        (int)Math.Round(y / Height, MidpointRounding.AwayFromZero);
}
