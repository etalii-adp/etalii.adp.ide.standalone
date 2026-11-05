namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>What reading a requirement document reports at one line (hierarchical-requirement.md, What reading reports).</summary>
/// <param name="Code">The finding's code: <c>std.missingId</c>, <c>std.containment</c>, <c>fbl.unknown-block</c> and so on.</param>
/// <param name="Line">The line it stands at, from 1, in the file that holds it.</param>
/// <param name="InRegistration">Whether it is a finding of the registration rather than of the Markdown file.</param>
public sealed record HierarchicalRequirementFinding(string Code, string Message, int Line, bool InRegistration = false);
