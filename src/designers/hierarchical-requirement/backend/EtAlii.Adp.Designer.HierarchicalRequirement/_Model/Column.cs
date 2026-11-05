namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The width the user set of one column of items: its depth, 1 for the top items, and its width in pixels
/// (hierarchical-requirement.des, Column; DESL 0.2 section 7.7.1).
/// </summary>
public sealed record Column(int Level, int Width);
