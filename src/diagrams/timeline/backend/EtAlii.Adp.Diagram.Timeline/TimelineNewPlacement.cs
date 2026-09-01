using System.Globalization;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The synthetic element id a canvas gesture uses to name a place where no element exists yet:
/// <c>new:{seconds},{row}</c>.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the context-action channel carries one element id per call and nothing
/// else - a drop's position and a relation-to-empty-space's landing point have no field of
/// their own. An element id is this module's to interpret: its resolver resolves ids, so it can
/// resolve one that names "the element about to exist here" as readily as one that names an
/// element that does. The id never reaches a selection, a file or the history - it lives for
/// exactly one ExecuteAction call.
/// </para>
/// <para>
/// Module-internal by construction: core resolves the id through this module's own resolver
/// and learns nothing.
/// </para>
/// </remarks>
public static class TimelineNewPlacement
{
    private const string Prefix = "new:";

    /// <summary>The id for a placement, as the canvas writes it.</summary>
    public static string IdFor(double seconds, int row) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}{seconds},{row}");

    /// <summary>Whether <paramref name="elementId"/> is a placement id, and what it carries.</summary>
    public static bool TryParse(string elementId, out double seconds, out int row)
    {
        seconds = 0;
        row = 0;

        if (elementId is null || !elementId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = elementId[Prefix.Length..].Split(',');
        return parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out row);
    }
}
