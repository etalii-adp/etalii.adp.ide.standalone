namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>A state of the state list: its name and its colour (hierarchical-requirement.des, State).</summary>
/// <param name="Colour">One of the Colour enumeration's names, <c>default</c> when the list does not give one.</param>
public sealed record State(string Name, string Colour);
