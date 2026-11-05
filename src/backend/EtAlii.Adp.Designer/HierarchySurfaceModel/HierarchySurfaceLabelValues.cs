namespace EtAlii.Adp.Designer;

/// <summary>The values a node carries of one label, as the node carries them, in the order of its line.</summary>
/// <param name="Label">The label's attribute in the specification.</param>
public sealed record HierarchySurfaceLabelValues(string Label, IReadOnlyList<string> Values);
