using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Sets the Description of an element or a connection - prose kept in the document and never drawn.
/// An empty one removes the key.
/// </summary>
public sealed record SetFdgDescriptionCommand(string BodyPath, string Id, string Description) : ICommand;
