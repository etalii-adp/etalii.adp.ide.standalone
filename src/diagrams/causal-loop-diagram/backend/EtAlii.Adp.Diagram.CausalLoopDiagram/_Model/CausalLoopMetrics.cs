using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The declared font metric this layout sizes variables with.
/// </summary>
/// <remarks>
/// Real metrics exist only in the browser; these are the module's estimate, and the client draws
/// each variable at the size the backend computed rather than re-measuring - the discipline the
/// mindmap module arrived at and c4 adopted from the start. The text's own width is the shared
/// <see cref="TextMetric"/>; the padding and minimum around it are this module's.
/// </remarks>
/// <param name="FontSize">Nominal size in canvas units.</param>
/// <param name="HorizontalPadding">Space either side of the text.</param>
/// <param name="VerticalPadding">Space above and below it.</param>
/// <param name="MinimumWidth">The narrowest a variable is drawn, so a one-letter name is still a target.</param>
/// <param name="Separation">The clear space kept between neighbours on the ring.</param>
public sealed record CausalLoopMetrics(
    double FontSize = 14,
    double HorizontalPadding = 14,
    double VerticalPadding = 10,
    double MinimumWidth = 90,
    double Separation = 40)
{
    /// <summary>The default this module lays out with.</summary>
    public static CausalLoopMetrics Default { get; } = new();

    /// <summary>How wide a variable showing <paramref name="text"/> is drawn.</summary>
    public double WidthOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Math.Max(MinimumWidth, TextMetric.WidthOf(text, FontSize) + (HorizontalPadding * 2));
    }

    /// <summary>How tall every variable is drawn - one line of text, so one height for all of them.</summary>
    public double Height => (FontSize * 1.4) + (VerticalPadding * 2);
}
