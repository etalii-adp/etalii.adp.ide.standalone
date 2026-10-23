namespace EtAlii.Adp.Designer;

/// <summary>A member of a label's list.</summary>
/// <param name="Id">What a gesture names it by, the specification's id of the member.</param>
/// <param name="Colour">One of the ten colour names: default, gray, brown, orange, yellow, green, blue, purple, pink, red.</param>
/// <param name="Implicit">Whether a node carries it and the list does not hold it.</param>
public sealed record HierarchySurfaceOption(string Id, string Name, string Colour, bool Implicit);
