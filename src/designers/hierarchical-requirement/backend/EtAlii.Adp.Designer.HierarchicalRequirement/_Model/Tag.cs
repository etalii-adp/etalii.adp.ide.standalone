namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>A tag of the tag list: its name and its colour (hierarchical-requirement.des, Tag).</summary>
/// <param name="Colour">One of the Colour enumeration's names, <c>default</c> when the list does not give one.</param>
public sealed record Tag(string Name, string Colour);
