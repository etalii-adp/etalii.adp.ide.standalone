namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// The declared font metric the layout sizes nodes with (Requirement 5.6). Real metrics exist
/// only in the browser; these are the module's estimate of them, and the client draws boxes
/// at the size the backend computed from them rather than re-measuring (Requirement 5.7).
/// </summary>
/// <param name="FontFamily">What the canvas renders node text in; carried so client and backend name the same face.</param>
/// <param name="FontSize">In canvas units (CSS pixels).</param>
/// <param name="AverageAdvance">The estimated width of one character, as a fraction of <paramref name="FontSize"/>.</param>
/// <param name="LineHeight">As a fraction of <paramref name="FontSize"/>.</param>
/// <param name="HorizontalPadding">Inside the node box, each side.</param>
/// <param name="VerticalPadding">Inside the node box, top and bottom.</param>
/// <param name="HorizontalGap">Between a parent's edge and its children's edge.</param>
/// <param name="VerticalGap">Between sibling subtrees.</param>
/// <param name="MinimumWidth">So an empty node is still something to click on.</param>
public sealed record MindmapMetrics(
    string FontFamily = "system-ui, sans-serif",
    double FontSize = 14,
    double AverageAdvance = 0.55,
    double LineHeight = 1.4,
    double HorizontalPadding = 10,
    double VerticalPadding = 6,
    double HorizontalGap = 48,
    double VerticalGap = 10,
    double MinimumWidth = 32)
{
    public static MindmapMetrics Default { get; } = new();

    /// <summary>The box a node with <paramref name="text"/> occupies.</summary>
    public MindmapSize Measure(string text)
    {
        var characters = text.Length;
        var width = Math.Max(MinimumWidth, characters * FontSize * AverageAdvance + 2 * HorizontalPadding);
        var height = FontSize * LineHeight + 2 * VerticalPadding;
        return new MindmapSize(Math.Round(width, 2), Math.Round(height, 2));
    }
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
