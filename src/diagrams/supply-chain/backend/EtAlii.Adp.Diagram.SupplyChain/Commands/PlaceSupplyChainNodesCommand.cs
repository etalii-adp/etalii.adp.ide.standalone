using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Places nodes so each one's TOP-LEFT is the given point - one node dragged, a group's members, or everything arranged.</summary>
public sealed record PlaceSupplyChainNodesCommand(string BodyPath, IReadOnlyDictionary<string, (double X, double Y)> Places) : ICommand;
