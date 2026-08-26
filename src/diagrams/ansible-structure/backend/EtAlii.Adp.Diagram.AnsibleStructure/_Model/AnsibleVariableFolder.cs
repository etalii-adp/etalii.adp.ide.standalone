namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// A <c>group_vars</c> or <c>host_vars</c> folder, as a node of its own (Requirement 4.1).
/// </summary>
/// <remarks>
/// The honest half of ansible-viz's idea. That these files exist and feed this inventory is a
/// fact; which variable a given play actually reads is an inference, and Requirement 5.8 refuses
/// to draw inferences as edges.
/// </remarks>
/// <param name="Name"><c>group_vars</c> or <c>host_vars</c>.</param>
/// <param name="RelativePath">The folder, relative to the diagram's folder.</param>
/// <param name="FileCount">How many files it holds.</param>
public sealed record AnsibleVariableFolder(string Name, string RelativePath, int FileCount);
