using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>One trend entry, and the lines that declare it.</summary>
/// <param name="Id">Its id. Empty when the entry states none, which the rules report.</param>
/// <param name="Name">Its name, the one label drawn.</param>
/// <param name="Start">Where the banner begins, as a month index; null when missing or malformed.</param>
/// <param name="Stop">Where the banner ends, as a month index; null when missing or malformed.</param>
/// <param name="Row">Its row; the top edge is <see cref="GhgScale.TopOf"/> of it.</param>
/// <param name="Phases">How many phases are visible, as written. Outside 1 to 4 opens and is reported.</param>
/// <param name="DraggedEnds">
/// The stored inner boundaries, one slot per boundary: 0 is <c>peak-end</c>, 1 <c>trough-end</c>,
/// 2 <c>slope-end</c>. A slot is null when that boundary was never dragged, which is what keeps it
/// out of the document (Requirement 2.2).
/// </param>
/// <param name="Tags">Its tags, in document order.</param>
/// <param name="Description">Prose about the trend. <b>Never sent to the client</b> (Requirement 12.3).</param>
/// <param name="Range">The lines this entry occupies, which is what an edit rewrites and nothing else.</param>
public sealed record GhgTrend(
    string Id,
    string Name,
    int? Start,
    int? Stop,
    int Row,
    int Phases,
    IReadOnlyList<int?> DraggedEnds,
    IReadOnlyList<string> Tags,
    string Description,
    LineRange Range)
{
    /// <summary>Whether both dates read and the stop is after the start - whether it can be drawn at all.</summary>
    public bool HasSpan => Start is { } start && Stop is { } stop && stop > start;

    /// <summary>The span in months; zero when <see cref="HasSpan"/> is false.</summary>
    public int Months => HasSpan ? Stop!.Value - Start!.Value : 0;

    /// <summary>The phase count clamped into 1 to 4, which is what is drawn.</summary>
    public int VisiblePhases => Math.Clamp(Phases, 1, GhgPhases.Count);
}
