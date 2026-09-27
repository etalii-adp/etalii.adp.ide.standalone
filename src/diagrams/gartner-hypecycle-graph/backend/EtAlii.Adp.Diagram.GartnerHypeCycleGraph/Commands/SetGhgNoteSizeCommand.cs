using System.Globalization;
using System.Text.RegularExpressions;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Resizes a note, as the canvas's resize sends it: <c>width x height</c>, optionally followed by
/// <c>at YYYY-MM</c> and <c>row N</c> - where its top-left now is, which a drag of the left or the
/// top border moves.
/// </summary>
/// <param name="BodyPath">The document.</param>
/// <param name="NoteId">The note.</param>
/// <param name="Size">Such as <c>200 x 64</c>, or <c>200 x 64 at 1950-01 row 3</c>.</param>
public sealed partial record SetGhgNoteSizeCommand(string BodyPath, string NoteId, string Size) : ICommand
{
    /// <summary>The size a value states, and the month and row when it states them; null when it does not read.</summary>
    public static (double Width, double Height, int? At, int? Row)? Parse(string? size)
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

        int? row = match.Groups["row"].Success && int.TryParse(match.Groups["row"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

        return (width, height, at, row);
    }

    /// <summary>A size as the grid shows it and a resize sends it.</summary>
    public static string Format(double width, double height) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(width, 2):0.##} x {Math.Round(height, 2):0.##}");

    [GeneratedRegex(@"^\s*(?<width>[0-9.]+)\s*x\s*(?<height>[0-9.]+)\s*(at\s+(?<at>\S+))?\s*(row\s+(?<row>-?[0-9]+))?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SizeExpression();
}
