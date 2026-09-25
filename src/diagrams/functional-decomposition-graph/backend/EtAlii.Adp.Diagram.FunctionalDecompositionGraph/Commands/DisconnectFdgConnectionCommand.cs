using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Removes one connection.</summary>
public sealed record DisconnectFdgConnectionCommand(string BodyPath, string ConnectionId) : ICommand;
