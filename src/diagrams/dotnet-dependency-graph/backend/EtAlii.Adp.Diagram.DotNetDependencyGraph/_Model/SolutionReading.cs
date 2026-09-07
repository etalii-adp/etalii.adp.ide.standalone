namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// What one read of a solution produced: the projects that resolved, and everything that did
/// not. Both halves are always present, because "the diagram opens showing what it could
/// resolve" (Requirement 2.4) needs the successes and the failures in the same answer.
/// </summary>
public sealed record SolutionReading(
    IReadOnlyList<SolutionProject> Projects,
    IReadOnlyList<SolutionFailure> Failures)
{
    /// <summary>A read that produced nothing but a reason - an unreadable or absent solution.</summary>
    public static SolutionReading OfFailure(string path, string reason) =>
        new([], [new SolutionFailure(path, reason)]);
}
