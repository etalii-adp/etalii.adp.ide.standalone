namespace EtAlii.Adp.Documents;

/// <summary>
/// How wide a text is estimated to be, one way on both tiers (backend-centralization R10): its
/// characters times the font size times the average advance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Real metrics exist only in the browser.</b> The backend sizes a box for a text and the client
/// draws the text inside it without re-measuring, so the estimate is only good if both tiers make
/// the same one. The values are the ones ansible-structure, c4, causal-loop and mindmap already
/// shared before this class existed: 14 and 0.55.
/// </para>
/// <para>
/// <b>This is the metric and nothing else.</b> The padding a module puts around the text, and the
/// minimum and maximum it clamps the box to, stay that module's own declared values (R10.2), so a
/// module that sizes its boxes differently is not made wrong by sharing the metric.
/// </para>
/// <para>
/// <b>A character is a UTF-16 code unit</b> - what <see cref="string.Length"/> counts, and what
/// JavaScript's <c>length</c> counts too - so a character outside the Basic Multilingual Plane
/// counts as two on both tiers.
/// </para>
/// <para>
/// The golden fixture both suites read is <c>src/fixtures/cross-tier/text-metric.json</c>.
/// </para>
/// </remarks>
public static class TextMetric
{
    /// <summary>The font size the measuring modules lay out with, in canvas units (CSS pixels).</summary>
    public const double DefaultFontSize = 14;

    /// <summary>The estimated width of one character, as a fraction of the font size.</summary>
    public const double AverageAdvance = 0.55;

    /// <summary>
    /// The estimated width of <paramref name="text"/> at <paramref name="fontSize"/>, with no
    /// padding, minimum or maximum applied.
    /// </summary>
    public static double WidthOf(string text, double fontSize = DefaultFontSize)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Length * fontSize * AverageAdvance;
    }
}
