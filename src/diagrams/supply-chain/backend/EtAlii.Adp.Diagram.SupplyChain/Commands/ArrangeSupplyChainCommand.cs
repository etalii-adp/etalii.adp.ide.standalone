using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Lays the whole diagram out left to right and writes every node's position.</summary>
public sealed record ArrangeSupplyChainCommand(string BodyPath) : ICommand;
