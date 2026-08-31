using System.Globalization;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Reads a time the way the whole module reads times: at face value, offset-zero.
/// </summary>
/// <remarks>
/// One place rather than a style flag at each call site, because the wrong default -
/// <see cref="DateTimeStyles.None"/>, which assumes the <b>local machine's</b> offset - reads
/// naturally and shipped here once already. See the parser's remarks for how it was caught.
/// </remarks>
public static class TimelineInstants
{
    /// <summary>The instant <paramref name="text"/> names, or null when it will not read.</summary>
    public static DateTimeOffset? Parse(string text) =>
        DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
}
