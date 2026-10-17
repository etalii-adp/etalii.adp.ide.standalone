using EtAlii.Adp.Layout;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// The viewport test of the element mappers, here rather than on <see cref="DiagramViewport"/> so that
/// the diagram abstractions need no geometry project (code-structure T157, T158).
/// </summary>
public static class DiagramViewportExtensions
{
    /// <summary>
    /// Whether <paramref name="box"/> is in view: a box the viewport merely touches counts, so an
    /// element on the edge is sent rather than popping in a pixel later (code-structure FR-045).
    /// </summary>
    public static bool Intersects(this DiagramViewport viewport, Rect box) =>
        box.Right >= viewport.MinX && box.X <= viewport.MaxX && box.Bottom >= viewport.MinY && box.Y <= viewport.MaxY;
}
