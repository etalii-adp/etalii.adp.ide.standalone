namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// The numbers the layout arranges by. Estimates of text metrics that only exist in a browser,
/// declared here so the backend and the canvas name the same sizes.
/// </summary>
/// <param name="FontSize">In canvas units (CSS pixels).</param>
/// <param name="AverageAdvance">The estimated width of one character, as a fraction of <paramref name="FontSize"/>.</param>
/// <param name="HorizontalPadding">Inside a node box, each side.</param>
/// <param name="NodeHeight">Every node is one line tall; this type draws no multi-line boxes.</param>
/// <param name="MinimumWidth">So a short name is still something to click on.</param>
/// <param name="RankGap">Between one rank's right edge and the next rank's left edge.</param>
/// <param name="RowGap">Between two nodes stacked within one rank.</param>
/// <param name="BandGap">
/// Between the execution flow and the inventory band beneath it. Wider than
/// <paramref name="RowGap"/> on purpose: the band is a different kind of thing, and the space
/// is what says so before any label does (Requirement 6.2).
/// </param>
public sealed record AnsibleMetrics(
    double FontSize = 14,
    double AverageAdvance = 0.55,
    double HorizontalPadding = 12,
    double NodeHeight = 32,
    double MinimumWidth = 72,
    double RankGap = 96,
    double RowGap = 24,
    double BandGap = 96)
{
    public static AnsibleMetrics Default { get; } = new();

    /// <summary>The width a node labelled <paramref name="text"/> needs.</summary>
    public double Measure(string? text) =>
        Math.Max(MinimumWidth, (text ?? "").Length * FontSize * AverageAdvance + (2 * HorizontalPadding));
}
