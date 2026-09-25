using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Moves an element so its TOP-LEFT is (<paramref name="X"/>, <paramref name="Y"/>) - the document's own coordinates.</summary>
public sealed record SetFdgPlacementCommand(string BodyPath, string ElementId, double X, double Y) : ICommand;
