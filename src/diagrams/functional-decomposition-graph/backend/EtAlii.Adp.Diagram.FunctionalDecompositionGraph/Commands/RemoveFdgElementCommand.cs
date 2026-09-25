using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Removes an element and every connection to or from it, in one edit, so one undo restores them all.</summary>
public sealed record RemoveFdgElementCommand(string BodyPath, string ElementId) : ICommand;
