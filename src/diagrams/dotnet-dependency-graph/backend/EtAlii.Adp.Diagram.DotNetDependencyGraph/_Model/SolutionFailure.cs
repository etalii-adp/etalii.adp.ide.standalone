namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Something the solution named that could not be read. Carried rather than thrown: the
/// diagram opens with what resolved, and this becomes a problem in the panel (Requirement 2.4).
/// </summary>
/// <param name="Path">What the solution named, as it named it - the string the user can search for.</param>
/// <param name="Reason">Why it did not resolve, in a sentence a reader can act on.</param>
public sealed record SolutionFailure(string Path, string Reason);
