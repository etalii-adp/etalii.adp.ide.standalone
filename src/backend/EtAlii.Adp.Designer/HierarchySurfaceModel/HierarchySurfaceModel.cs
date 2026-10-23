namespace EtAlii.Adp.Designer;

/// <summary>
/// The read model of a hierarchy surface (DESL section 7.7, spec 029 R8): everything a page of nested boxes shows,
/// sent whole on open and again after every change. It knows no designer type: a module fills it from its own model,
/// naming its levels, entries, labels and notes by the words of its specification.
/// </summary>
/// <remarks>
/// It carries no focus, narrowing, sort or selection: those are the client's and never written to a file (FR-008).
/// </remarks>
/// <param name="Title">The document's title, for the trail.</param>
/// <param name="ReadOnlyReason">Why nothing can be edited, or empty when the document can be.</param>
/// <param name="Cause">What this model follows: one of <see cref="HierarchySurfaceCauses"/>.</param>
/// <param name="Nodes">Every level and entry, in the file's order, each naming its parent.</param>
/// <param name="OptionLists">The members of each label's list.</param>
/// <param name="Filters">The stored filters, in their order.</param>
/// <param name="Notes">Every note, open and resolved, in the file's order.</param>
/// <param name="Findings">Everything reading reported, whatever the client narrows to.</param>
public sealed record HierarchySurfaceModel(
    string Title,
    string ReadOnlyReason,
    string Cause,
    IReadOnlyList<HierarchySurfaceNode> Nodes,
    IReadOnlyList<HierarchySurfaceOptionList> OptionLists,
    IReadOnlyList<HierarchySurfaceFilter> Filters,
    IReadOnlyList<HierarchySurfaceNote> Notes,
    IReadOnlyList<HierarchySurfaceFinding> Findings);
