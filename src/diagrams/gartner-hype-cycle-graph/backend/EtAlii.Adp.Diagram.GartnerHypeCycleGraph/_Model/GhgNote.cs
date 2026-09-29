using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>One note entry - the author's own remark, in a box on the canvas - and the lines that declare it.</summary>
/// <param name="Id">Its id. Empty when the entry states none, which the rules report.</param>
/// <param name="Text">Its text, line breaks and all.</param>
/// <param name="At">The month of its left edge, as a month index; null when missing or malformed, which <c>ghg.note-position</c> reports.</param>
/// <param name="Row">The row of its top edge.</param>
/// <param name="Width">Its width in canvas units; null when missing or not a number.</param>
/// <param name="Height">Its height in canvas units; null when missing or not a number.</param>
/// <param name="Range">The lines this entry occupies.</param>
/// <remarks>
/// <b>A note's size is in canvas units, not in time</b>: it is a box of text, so a document whose
/// unit changes redraws its trends at a new scale and leaves its notes their size.
/// </remarks>
public sealed record GhgNote(
    string Id,
    string Text,
    int? At,
    int Row,
    double? Width,
    double? Height,
    LineRange Range)
{
    /// <summary>Whether it has a readable position and a positive size - whether it can be drawn at all.</summary>
    public bool IsPlaceable => At is not null && Width is > 0 && Height is > 0;
}
