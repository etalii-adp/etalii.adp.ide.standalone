using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Adds one empty group whose frame is centred on (<paramref name="X"/>, <paramref name="Y"/>).</summary>
/// <param name="GroupId">Empty to have one minted; the handler mints it once and returns the minted command as the redo.</param>
public sealed record AddSupplyChainGroupCommand(string BodyPath, double X, double Y, string GroupId = "") : ICommand;
