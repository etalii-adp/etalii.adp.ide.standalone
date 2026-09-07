namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// What one read of a project file produced. Failures travel with the readings for the same
/// reason the solution's do: the graph is built from what resolved and the rest is reported.
/// </summary>
public sealed record ProjectReading(
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<ProjectReferenceReading> ProjectReferences,
    IReadOnlyList<PackageReferenceReading> PackageReferences,
    IReadOnlyList<SolutionFailure> Failures)
{
    /// <summary>A read that produced nothing but a reason - an unreadable project file.</summary>
    public static ProjectReading OfFailure(string path, string reason) =>
        new([], [], [], [new SolutionFailure(path, reason)]);
}
