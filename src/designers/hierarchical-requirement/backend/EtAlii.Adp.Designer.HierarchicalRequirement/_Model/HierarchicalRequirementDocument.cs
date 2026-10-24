namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// A requirement document as the designer holds it: the marks of its counters, its entries and assessments in reading
/// order, the members of its lists, its stored filters and the widths of its columns, and what reading it reported.
/// </summary>
/// <param name="LastItem">The mark of the item counter on line 2, <c>ITEM-0007</c>, or null when the file has none.</param>
/// <param name="LastRequirement">The mark of the requirement counter, or null when the file has none.</param>
/// <param name="ReadOnlyReason">Why the document cannot be changed here, or empty when it can.</param>
public sealed record HierarchicalRequirementDocument(
    string? LastItem,
    string? LastRequirement,
    IReadOnlyList<RequirementEntry> Entries,
    IReadOnlyList<Assessment> Assessments,
    IReadOnlyList<Tag> Tags,
    IReadOnlyList<Category> Categories,
    IReadOnlyList<State> States,
    IReadOnlyList<Filter> Filters,
    IReadOnlyList<Column> Columns,
    IReadOnlyList<HierarchicalRequirementFinding> Findings,
    string ReadOnlyReason);
