namespace EtAlii.Adp.Designer;

/// <summary>The members of one label's list: the stored ones in their order, then those only a node carries.</summary>
/// <param name="Label">The label's attribute in the specification.</param>
public sealed record HierarchySurfaceOptionList(string Label, IReadOnlyList<HierarchySurfaceOption> Options);
