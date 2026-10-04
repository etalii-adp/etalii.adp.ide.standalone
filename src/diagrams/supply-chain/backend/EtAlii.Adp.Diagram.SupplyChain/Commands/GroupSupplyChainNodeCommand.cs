using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Creates a group named <paramref name="GroupName"/> and puts the node inside it.</summary>
/// <param name="GroupId">Empty to have one minted; the handler mints it once and returns the minted command as the redo.</param>
public sealed record GroupSupplyChainNodeCommand(string BodyPath, string NodeId, string GroupName, string GroupId = "") : ICommand;
