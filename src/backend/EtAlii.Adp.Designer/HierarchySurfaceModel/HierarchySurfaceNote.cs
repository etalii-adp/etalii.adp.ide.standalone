namespace EtAlii.Adp.Designer;

/// <summary>A note about one node: a question, a remark, a warning or an idea (DESL section 7.7.1, <c>notes</c>).</summary>
/// <param name="Id">What a gesture names it by: its place among the notes, valid for this model only.</param>
/// <param name="Kind">Its kind, as the specification names it.</param>
/// <param name="About">The number it is about, as written.</param>
/// <param name="AboutId">The node it is about, or empty when it names none.</param>
/// <param name="Detail">The lines under it, empty when there are none.</param>
/// <param name="References">The numbers its text names, then those its detail names, offset after the text and one LF.</param>
public sealed record HierarchySurfaceNote(
    string Id,
    string Kind,
    string About,
    string AboutId,
    string Text,
    string Detail,
    bool Resolved,
    IReadOnlyList<HierarchySurfaceReference> References);
