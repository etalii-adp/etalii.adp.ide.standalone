namespace EtAlii.Adp.Designer;

/// <summary>A number a text names, where it stands in that text.</summary>
/// <param name="Start">Its offset, in UTF-16 code units.</param>
/// <param name="Length">The length of the bracketed number, brackets included.</param>
/// <param name="TargetId">The node it names, or empty when it names none.</param>
public sealed record HierarchySurfaceReference(int Start, int Length, string TargetId);
