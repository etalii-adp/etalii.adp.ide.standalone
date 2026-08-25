namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>A registration file that could not be routed, and why.</summary>
/// <param name="Reason">
/// A whole sentence, shown to the user as the problem. It is carried rather than inferred
/// because the two ways to get here read very differently: a file that genuinely could not be
/// opened, and one that opened fine but names a document outside the project. Reporting the
/// second as the first sends a reader looking for a permissions fault that is not there.
/// </param>
public sealed record DiagramUnreadable(string Path, string Reason) : DiagramRouting;
