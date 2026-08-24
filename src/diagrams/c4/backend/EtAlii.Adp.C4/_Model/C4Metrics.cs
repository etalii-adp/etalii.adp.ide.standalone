namespace EtAlii.Adp.C4;

/// <summary>Where an element sits and how big it is, in canvas units with the view's origin at (0,0).</summary>
public readonly record struct C4Box(double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>Whether this box and <paramref name="other"/> share any area.</summary>
    public bool Overlaps(C4Box other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

/// <summary>
/// The declared font metric the layout sizes C4 boxes with. Real metrics exist only in the
/// browser; these are the module's estimate, and the client draws each box at the size the
/// backend computed rather than re-measuring - the discipline the mindmap module arrived at,
/// applied here from the start.
/// </summary>
/// <remarks>
/// A C4 box carries three things, which is why it is taller than a mindmap node: the name, the
/// bracketed type-and-technology line, and the description (c4-diagrams Requirement 4.1).
/// </remarks>
public sealed record C4Metrics(
    string FontFamily = "system-ui, sans-serif",
    double FontSize = 14,
    double AverageAdvance = 0.55,
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
    public C4Box Measure(string name, string typeLine, string description)
    {
        var contentWidth = MaximumWidth - 2 * HorizontalPadding;
        var widest = Math.Max(TextWidth(name), TextWidth(typeLine));
        var width = Math.Clamp(Math.Max(widest, Math.Min(TextWidth(description), contentWidth)) + 2 * HorizontalPadding, MinimumWidth, MaximumWidth);

        var descriptionLines = description.Length == 0 ? 0 : (int)Math.Ceiling(TextWidth(description) / (width - 2 * HorizontalPadding));
        var lines = 1 + (typeLine.Length > 0 ? 1 : 0) + Math.Max(0, descriptionLines);
        var height = lines * FontSize * LineHeight + 2 * VerticalPadding;

        return new C4Box(0, 0, Math.Round(width, 2), Math.Round(height, 2));
    }

    private double TextWidth(string text) => text.Length * FontSize * AverageAdvance;
}
