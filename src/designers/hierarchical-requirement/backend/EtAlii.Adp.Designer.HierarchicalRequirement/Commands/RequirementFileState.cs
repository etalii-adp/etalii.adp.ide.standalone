namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>One file of a step: what it held before the step, and what the step left it holding.</summary>
/// <param name="Path">The body or the registration.</param>
/// <param name="Before">Its bytes before the step.</param>
/// <param name="After">Its bytes after the step; a file that holds others was written by another program since.</param>
internal sealed record RequirementFileState(string Path, byte[] Before, byte[] After);
