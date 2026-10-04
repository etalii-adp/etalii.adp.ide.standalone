using System.Text.RegularExpressions;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// The colours a node or a flow may be: a word from the palette, or a colour written outright.
/// </summary>
/// <remarks>
/// <para>
/// <b>A palette word is painted by the theme</b> - <c>--color-diagram-sankey-&lt;word&gt;</c> in
/// <c>index.css</c>, once for the light theme and once for the dark - so a diagram written with
/// words reads in both. <b>A hex colour is painted as written</b>, in both themes, because it is the
/// document's own choice and the module has no business adjusting it.
/// </para>
/// <para>
/// The words name colours and nothing else: a Sankey diagram is not about any one subject, so the
/// palette says what a band looks like, never what it means.
/// </para>
/// </remarks>
public static partial class SankeyColors
{
    /// <summary>What a node that states no colour is drawn in.</summary>
    public const string Default = "grey";

    /// <summary>The palette, in the order the property grid offers it.</summary>
    public static readonly IReadOnlyList<string> Palette =
        ["grey", "slate", "purple", "blue", "teal", "green", "lime", "yellow", "orange", "red", "pink", "brown"];

    /// <summary>Whether the text is a palette word.</summary>
    public static bool IsPaletteWord(string color) => Palette.Contains(color, StringComparer.Ordinal);

    /// <summary>Whether the text is a colour written outright: <c>#rgb</c> or <c>#rrggbb</c>.</summary>
    public static bool IsHex(string color) => color is { Length: > 0 } && HexPattern().IsMatch(color);

    /// <summary>Whether the text is a colour this module can paint, the empty default included.</summary>
    public static bool IsKnown(string color) => color.Length == 0 || IsPaletteWord(color) || IsHex(color);

    /// <summary>
    /// The colour to paint, as the payload carries it: the palette word, or the hex, and the other
    /// empty. Anything else - an unknown word - paints the default, and the rules report it.
    /// </summary>
    public static (string Word, string Hex) Resolve(string color, string fallback = Default)
    {
        if (IsPaletteWord(color))
        {
            return (color, "");
        }

        if (IsHex(color))
        {
            return ("", color.ToLowerInvariant());
        }

        return IsPaletteWord(fallback) ? (fallback, "") : IsHex(fallback) ? ("", fallback.ToLowerInvariant()) : (Default, "");
    }

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexPattern();
}
