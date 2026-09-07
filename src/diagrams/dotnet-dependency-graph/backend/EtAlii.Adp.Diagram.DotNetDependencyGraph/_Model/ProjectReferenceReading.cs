namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// One <c>ProjectReference</c> as the project file declares it.
/// </summary>
/// <param name="Include">The declaration verbatim - what the user would search the file for.</param>
/// <param name="AbsolutePath">
/// Where it points, resolved. The graph matches this against the solution's own projects, so
/// that two spellings of one path do not become two nodes.
/// </param>
public sealed record ProjectReferenceReading(string Include, string AbsolutePath);
