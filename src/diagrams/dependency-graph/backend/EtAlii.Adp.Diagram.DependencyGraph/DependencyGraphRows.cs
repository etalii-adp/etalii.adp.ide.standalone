using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// The only conversion between a row index and a vertical coordinate, and the nearest-row inverse
/// a drag snaps through.
/// </summary>
/// <remarks>
/// Copied from the timeline unchanged, because rows were never time. A row is an integer the
/// author owns - negative is as valid as positive, and gaps are the author's business. The height
/// is this module's rendering constant, not data: it appears in the payload's y only as
/// <c>row × Height</c>, and the client divides it straight back out, so changing it re-spaces
/// every diagram without touching a single file.
/// </remarks>
public static class DependencyGraphRows
{
    /// <summary>The vertical distance between adjacent rows, in the module's own y units.</summary>
    public const double Height = 60d;

    /// <summary>The y for a row.</summary>
    public static double ToY(int row) => row * Height;

    /// <summary>
    /// The row nearest a y - what a vertical drag snaps to on release.
    /// </summary>
    /// <remarks>
    /// The rule is <see cref="RowRounding.ToNearestRow"/>, shared with every row canvas and pinned by
    /// the golden fixture the client reads too (backend-centralization R9): halves round away from
    /// zero, so the midpoint between two rows resolves the same way on every row and on both sides
    /// of the origin.
    /// </remarks>
    public static int ToNearestRow(double y) => RowRounding.ToNearestRow(y, Height);
}
