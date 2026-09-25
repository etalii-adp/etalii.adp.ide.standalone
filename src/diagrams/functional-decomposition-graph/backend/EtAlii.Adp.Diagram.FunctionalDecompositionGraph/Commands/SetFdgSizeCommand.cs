using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Resizes an element: a width for any of the five, and a height for a Comment only. Both are clamped
/// to their minimums rather than refused.
/// </summary>
public sealed record SetFdgSizeCommand(string BodyPath, string ElementId, double Width, double? Height = null) : ICommand;
