namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>A category of the category list: its name and its colour (hierarchical-requirement.des, Category).</summary>
/// <param name="Colour">One of the Colour enumeration's names, <c>default</c> when the list does not give one.</param>
public sealed record Category(string Name, string Colour);
