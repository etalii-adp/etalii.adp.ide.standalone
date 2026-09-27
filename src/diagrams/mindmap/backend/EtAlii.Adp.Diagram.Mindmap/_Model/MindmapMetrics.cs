using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// The declared font metric the layout sizes nodes with (Requirement 5.6). Real metrics exist
/// only in the browser; these are the module's estimate of them, and the client draws boxes
/// at the size the backend computed from them rather than re-measuring (Requirement 5.7).
/// The text's own width is the shared <see cref="TextMetric"/>; the padding and minimum around it are this module's.
/// </summary>
/// <param name="FontFamily">What the canvas renders node text in; carried so client and backend name the same face.</param>
/// <param name="FontSize">In canvas units (CSS pixels).</param>
/// <param name="LineHeight">As a fraction of <paramref name="FontSize"/>.</param>
/// <param name="HorizontalPadding">Inside the node box, each side.</param>
/// <param name="VerticalPadding">Inside the node box, top and bottom.</param>
/// <param name="HorizontalGap">Between a parent's edge and its children's edge, at minimum - see <paramref name="MinimumGapRatio"/>.</param>
/// <param name="VerticalGap">Between sibling subtrees, at minimum - see <paramref name="MinimumGapRatio"/>.</param>
/// <param name="MinimumWidth">So an empty node is still something to click on.</param>
/// <param name="MinimumGapRatio">
/// Elements keep at least this fraction of a node's width between them: the gap beside a node
/// is never smaller than its width times this ratio, so wide nodes get proportionally more
/// air. Configurable through appsettings.json's <c>Mindmap:MinimumGapRatio</c>.
/// </param>
public sealed record MindmapMetrics(
    string FontFamily = "system-ui, sans-serif",
    double FontSize = 14,
    double LineHeight = 1.4,
    double HorizontalPadding = 10,
    double VerticalPadding = 6,
    double HorizontalGap = 48,
    double VerticalGap = 10,
    double MinimumWidth = 32,
    double MinimumGapRatio = 0.1)
{
    public static MindmapMetrics Default { get; } = new();

    /// <summary>The box a node with <paramref name="text"/> occupies.</summary>
    public MindmapSize Measure(string text)
    {
        var width = Math.Max(MinimumWidth, TextMetric.WidthOf(text, FontSize) + 2 * HorizontalPadding);
        var height = FontSize * LineHeight + 2 * VerticalPadding;
        return new MindmapSize(Math.Round(width, 2), Math.Round(height, 2));
    }

    /// <summary>The gap between a node of <paramref name="width"/> and whatever sits beside it: the configured floor, or the ratio's share of the width when that is more.</summary>
    public double GapBeside(double width) => Math.Max(HorizontalGap, width * MinimumGapRatio);

    /// <summary>The gap between two stacked subtrees whose widest members are <paramref name="widthA"/> and <paramref name="widthB"/> wide.</summary>
    public double GapBetween(double widthA, double widthB) => Math.Max(VerticalGap, Math.Max(widthA, widthB) * MinimumGapRatio);
}

public readonly record struct MindmapSize(double Width, double Height);

/// <summary>Where a node sits: its top-left corner, and its size. Positions are in canvas units with the root's centre at the origin.</summary>
public readonly record struct MindmapBox(double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
    public double Right => X + Width;
    public double Bottom => Y + Height;
}
