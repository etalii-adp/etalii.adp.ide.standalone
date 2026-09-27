using System.Globalization;
using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>One influence entry, and the lines that declare it.</summary>
/// <param name="Id">Its id. Empty when the entry states none, which the rules report.</param>
/// <param name="From">The influencing trend's id.</param>
/// <param name="FromEnd">Where it leaves that trend.</param>
/// <param name="To">The influenced trend's id.</param>
/// <param name="ToEnd">Where it arrives at that trend.</param>
/// <param name="Description">Prose about the influence. <b>Never sent to the client</b>.</param>
/// <param name="Range">The lines this entry occupies.</param>
public sealed record GhgInfluence(
    string Id,
    string From,
    GhgEnd FromEnd,
    string To,
    GhgEnd ToEnd,
    string Description,
    LineRange Range);

/// <summary>Where one end of an influence attaches (Requirement 2.3), exactly as the document states it.</summary>
/// <param name="Phase">The phase's name, verbatim, so an unknown one survives and is reported.</param>
/// <param name="Edge"><c>top</c> or <c>bottom</c>, verbatim.</param>
/// <param name="At">The fraction along the phase's stretch of the edge; null when missing or not a number.</param>
public sealed record GhgEnd(string Phase, string Edge, double? At)
{
    /// <summary>The edge an influence attaches to at the top of a trend.</summary>
    public const string Top = "top";

    /// <summary>The edge an influence attaches to at the bottom of a trend.</summary>
    public const string Bottom = "bottom";

    /// <summary>The phase's index, or -1 when it names no phase.</summary>
    public int PhaseIndex => GhgPhases.IndexOf(Phase);

    /// <summary>Whether every part of the end reads: a known phase, a known edge, and an <c>at</c> in 0 to 1.</summary>
    public bool IsReadable =>
        PhaseIndex >= 0 &&
        Edge is Top or Bottom &&
        At is >= 0 and <= 1;

    /// <summary>The <c>at</c> as the document writes it: two decimals, invariant culture.</summary>
    public static string FormatAt(double at) => Math.Round(at, 2).ToString("0.0#", CultureInfo.InvariantCulture);

    /// <summary>The end as the property grid and a gesture write it: <c>phase/edge/at</c>.</summary>
    public override string ToString() => $"{Phase}/{Edge}/{(At is { } at ? FormatAt(at) : "")}";

    /// <summary>Reads <c>phase/edge/at</c>, refusing anything that is not a readable end.</summary>
    public static bool TryParse(string? text, out GhgEnd end)
    {
        end = new GhgEnd("", "", null);
        var parts = (text ?? "").Trim().Split('/');
        if (parts.Length != 3 ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var at))
        {
            return false;
        }

        end = new GhgEnd(parts[0].Trim(), parts[1].Trim(), Math.Round(at, 2));
        return end.IsReadable;
    }
}
