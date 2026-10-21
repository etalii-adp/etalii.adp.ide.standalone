namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// One project a solution names and which is actually on disk.
/// </summary>
/// <param name="Name">The project's file name without its extension - what the graph labels it.</param>
/// <param name="RelativePath">
/// The path relative to the solution, forward-slashed on every platform. This is what the
/// element id is built from, so a stored position survives the diagram being opened on a
/// different operating system.
/// </param>
/// <param name="AbsolutePath">Where to read the project file from. Never written to.</param>
public sealed record SolutionProject(string Name, string RelativePath, string AbsolutePath)
{
    /// <summary>The 1-based line of the project's entry in the solution; 0 when it is not known.</summary>
    public int Line { get; init; }

    /// <summary>
    /// The 1-based column of the entry's element name in a <c>.slnx</c>, as the XML reader counts it; 0 for a
    /// classic <c>.sln</c>, whose entry is a line and not an element.
    /// </summary>
    public int Column { get; init; }
}
