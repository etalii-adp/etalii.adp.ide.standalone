using EtAlii.Adp.Layout;

namespace EtAlii.Adp.Diagram;

/// <summary>The visible rectangle a connection reported, in the diagram's own units.</summary>
public readonly record struct DiagramViewport(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>The "everything is in view" viewport a connection has before it reports one.</summary>
    public static DiagramViewport Unbounded { get; } = new(
        double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity);

    /// <summary>
    /// Whether <paramref name="box"/> is in view: a box the viewport merely touches counts, so an
    /// element on the edge is sent rather than popping in a pixel later (code-structure FR-045).
    /// </summary>
    public bool Intersects(Rect box) =>
        box.Right >= MinX && box.X <= MaxX && box.Bottom >= MinY && box.Y <= MaxY;
}
