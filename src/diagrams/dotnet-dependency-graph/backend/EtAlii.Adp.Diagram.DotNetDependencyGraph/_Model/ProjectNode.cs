namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>One project of the solution, as the graph draws it.</summary>
/// <param name="Id">
/// <c>project:&lt;path relative to the solution&gt;</c>, forward-slashed. A stored position is
/// bound to this, so it is built from the path rather than from a display name: renaming the
/// assembly leaves the box where the user put it, moving the file does not - which follows from
/// this choice rather than from chance (Requirement 6.7).
/// </param>
/// <param name="TargetFrameworks">Every framework the project targets; multi-targeting is ordinary.</param>
/// <param name="DotNetVersion">
/// The .NET version where one is discoverable from the frameworks, else <c>null</c> - which the
/// grid renders as an explicit absence rather than as a blank (Requirement 4.4).
/// </param>
public sealed record ProjectNode(
    string Id,
    string Name,
    string RelativePath,
    IReadOnlyList<string> TargetFrameworks,
    string? DotNetVersion);
