namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// What one element of a map is called, and where it sits in the chain of names a selection
/// carries (Requirement 11.3).
/// </summary>
/// <param name="Text">The element's display text - a component's name, a link's two endpoints.</param>
/// <param name="Path">
/// The chain of names from the top of the map. One segment for almost everything: a Wardley map
/// is flat. A pipeline child is the exception - it is named beneath its parent.
/// </param>
/// <param name="HasChildren">True for a component that is a pipeline parent, and nothing else.</param>
public sealed record WardleyElementDescription(string Text, IReadOnlyList<string> Path, bool HasChildren);
