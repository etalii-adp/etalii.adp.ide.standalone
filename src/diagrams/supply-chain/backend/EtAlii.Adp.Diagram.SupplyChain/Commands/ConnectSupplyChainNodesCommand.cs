using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Adds a flow from <paramref name="From"/> to <paramref name="To"/>.</summary>
/// <param name="FlowId">Empty to have one minted; the handler mints it once and returns the minted command as the redo.</param>
public sealed record ConnectSupplyChainNodesCommand(string BodyPath, string From, string To, string FlowId = "") : ICommand;
