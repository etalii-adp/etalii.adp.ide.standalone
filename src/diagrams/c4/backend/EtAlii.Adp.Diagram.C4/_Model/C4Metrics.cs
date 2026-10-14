using EtAlii.Adp.Layout;

namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// The declared font metric the layout sizes C4 boxes with. Real metrics exist only in the
/// browser; these are the module's estimate, and the client draws each box at the size the
/// backend computed rather than re-measuring - the discipline the mindmap module arrived at,
/// applied here from the start. The text's own width is the shared <see cref="TextMetric"/>; the
/// padding, minimum and maximum around it are this module's.
/// </summary>
/// <remarks>
/// A C4 box carries three things, which is why it is taller than a mindmap node: the name, the
/// bracketed type-and-technology line, and the description (c4-diagrams Requirement 4.1).
/// </remarks>
public sealed record C4Metrics(
    double FontSize = 14,
    double LineHeight = 1.4,
    double HorizontalPadding = 12,
    double VerticalPadding = 10,
    double MinimumWidth = 160,
    double MaximumWidth = 240,
    double RankSeparation = 120,
    double NodeSeparation = 60,
    double BoundaryPadding = 32)
{
    public static C4Metrics Default { get; } = new();

    /// <summary>
    /// The box an element occupies, given the three lines it shows. The width is the widest
    /// line, clamped so one long description does not make a box the width of the diagram, and
    /// the height grows with the lines the description wraps onto.
    /// </summary>
    public Rect Measure(string name, string typeLine, string description)
    {
        var contentWidth = MaximumWidth - 2 * HorizontalPadding;
        var widest = Math.Max(TextWidth(name), TextWidth(typeLine));
        var width = Math.Clamp(Math.Max(widest, Math.Min(TextWidth(description), contentWidth)) + 2 * HorizontalPadding, MinimumWidth, MaximumWidth);

        var descriptionLines = description.Length == 0 ? 0 : (int)Math.Ceiling(TextWidth(description) / (width - 2 * HorizontalPadding));
        var lines = 1 + (typeLine.Length > 0 ? 1 : 0) + Math.Max(0, descriptionLines);
        var height = lines * FontSize * LineHeight + 2 * VerticalPadding;

        return new Rect(0, 0, Math.Round(width, 2), Math.Round(height, 2));
    }

    private double TextWidth(string text) => TextMetric.WidthOf(text, FontSize);
}
