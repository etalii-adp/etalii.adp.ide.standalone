namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The one place the document's axes become the canvas's (Requirement 5.2).
/// </summary>
/// <remarks>
/// <para>
/// The document writes <c>[visibility, maturity]</c>. The canvas draws <c>(x, y)</c>. The
/// mapping is a <b>transposition and an inversion at once</b>:
/// </para>
/// <list type="bullet">
/// <item>maturity is the <b>horizontal</b> axis, genesis at the left, so <c>x = maturity</c>.</item>
/// <item>visibility is the <b>vertical</b> axis with the user need at the <b>top</b>, and canvas
/// y grows downward, so <c>y = 1 - visibility</c>.</item>
/// </list>
/// <para>
/// Two mistakes are easy here and neither announces itself: swapping the pair reads a map
/// sideways, and forgetting the inversion turns it upside down. Both still render, which is why
/// this lives in one file with a round-trip test rather than being written out wherever it is
/// needed. Nothing else in this module - not the parser, not a command, not the canvas - may
/// do this conversion.
/// </para>
/// </remarks>
public static class WardleyAxis
{
    /// <summary>The canvas point for a document coordinate.</summary>
    public static (double X, double Y) ToPoint(WardleyCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return (coordinate.Maturity, 1d - coordinate.Visibility);
    }

    /// <summary>The document coordinate for a canvas point - the exact inverse of <see cref="ToPoint"/>.</summary>
    public static WardleyCoordinate ToCoordinate(double x, double y) => new(1d - y, x);

    /// <summary>
    /// A coordinate clamped into the map's bounded space (Requirement 7.3). A component cannot
    /// be more evolved than commodity, and the space has no outside.
    /// </summary>
    public static WardleyCoordinate Clamp(WardleyCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return new WardleyCoordinate(Math.Clamp(coordinate.Visibility, 0d, 1d), Math.Clamp(coordinate.Maturity, 0d, 1d));
    }
}
