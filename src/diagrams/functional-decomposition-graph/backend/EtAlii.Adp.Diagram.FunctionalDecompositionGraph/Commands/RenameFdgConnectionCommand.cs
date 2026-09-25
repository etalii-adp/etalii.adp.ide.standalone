using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Sets a connection's name, which the canvas draws at its midpoint when it is not empty.</summary>
public sealed record RenameFdgConnectionCommand(string BodyPath, string ConnectionId, string Name) : ICommand;
