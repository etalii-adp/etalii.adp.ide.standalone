namespace EtAlii.Adp.Documents;

/// <summary>
/// The nearest row for a vertical position on a row canvas: the one rounding rule a drag snaps
/// through, on both tiers (backend-centralization R9).
/// </summary>
/// <remarks>
/// <para>
/// <b>Halves round AWAY FROM ZERO</b>, which is not .NET's default: <see cref="Math.Round(double)"/>
/// rounds a half to the even neighbour, so the midpoint between rows 0 and 1 would snap down while
/// the midpoint between rows 1 and 2 snapped up - an off-by-one a user perceives as flaky snapping
/// rather than as a bug they could report. JavaScript's <c>Math.round</c> is wrong the other way,
/// rounding halves up, so a drag half a row above the origin lands a row low while the identical drag
/// below it lands correctly. Timeline and dependency-graph wrote this rule identically before it was
/// named here, and the client's <c>snapToStep</c> is the same rule on the other side of the wire.
/// </para>
/// <para>
/// <b>The golden fixture is the rule's specification.</b> <c>src/fixtures/cross-tier/row-rounding.json</c>
/// holds positions and the rows they land in, including exact halves either side of zero and negative
/// zero, and both suites read it. Change this rule and that file together, or one tier drifts from the
/// other silently.
/// </para>
/// <para>
/// A row is an <see cref="int"/>, so there is no negative-zero row on this side; the client, whose rows
/// are numbers, must normalise one away itself, and the fixture's negative-zero case is there to make it.
/// </para>
/// </remarks>
public static class RowRounding
{
    /// <summary>
    /// The row whose top is nearest <paramref name="y"/>, when rows are <paramref name="rowHeight"/>
    /// apart and row 0 lies at 0 - rounding a position exactly between two rows away from zero.
    /// </summary>
    /// <param name="y">The vertical position, in the module's own y units.</param>
    /// <param name="rowHeight">The distance between adjacent rows; positive and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rowHeight"/> is not a positive, finite number.</exception>
    public static int ToNearestRow(double y, double rowHeight)
    {
        if (!(rowHeight > 0) || double.IsPositiveInfinity(rowHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(rowHeight), rowHeight, "A row height must be a positive, finite number.");
        }

        return (int)Math.Round(y / rowHeight, MidpointRounding.AwayFromZero);
    }
}
