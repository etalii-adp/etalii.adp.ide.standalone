using System.Globalization;
using System.Text.RegularExpressions;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Resizes a note, as the canvas's resize sends it: <c>width x height</c>, followed by
/// <c>at YYYY-MM</c> when the left border moved and the note's left edge is at a new month.
/// </summary>
/// <param name="BodyPath">The document.</param>
/// <param name="NoteId">The note.</param>
/// <param name="Size">Such as <c>200 x 64</c> or <c>200 x 64 at 1950-01</c>.</param>
public sealed partial record SetGhgNoteSizeCommand(string BodyPath, string NoteId, string Size) : ICommand
{
    /// <summary>The size a value states, and the month when it states one; null when it does not read.</summary>
    public static (double Width, double Height, int? At)? Parse(string? size)
    {
        var match = SizeExpression().Match(size ?? "");
        if (!match.Success ||
            !double.TryParse(match.Groups["width"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(match.Groups["height"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
        {
            return null;
        }

        int? at = null;
        if (match.Groups["at"].Success)
        {
            at = GhgScale.ParseMonth(match.Groups["at"].Value);
            if (at is null)
            {
                return null;
            }
        }

        return (width, height, at);
    }

    /// <summary>A size as the grid shows it and a resize sends it.</summary>
    public static string Format(double width, double height) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(width, 2):0.##} x {Math.Round(height, 2):0.##}");

    [GeneratedRegex(@"^\s*(?<width>[0-9.]+)\s*x\s*(?<height>[0-9.]+)\s*(at\s+(?<at>\S+))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SizeExpression();
}
