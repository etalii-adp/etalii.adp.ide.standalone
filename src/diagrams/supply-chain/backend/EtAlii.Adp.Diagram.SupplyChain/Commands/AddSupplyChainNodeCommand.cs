using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Adds one node of stage <paramref name="NodeType"/> centred on (<paramref name="X"/>, <paramref name="Y"/>).</summary>
/// <param name="NodeId">Empty to have one minted; the handler mints it once and returns the minted command as the redo.</param>
public sealed record AddSupplyChainNodeCommand(string BodyPath, string NodeType, double X, double Y, string NodeId = "") : ICommand;
