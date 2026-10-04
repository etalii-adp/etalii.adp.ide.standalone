using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>
/// Moves a node or a group's frame so its top-left is (<paramref name="X"/>, <paramref name="Y"/>) -
/// what a drag on the canvas commits.
/// </summary>
public sealed record MoveSupplyChainEntryCommand(string BodyPath, string EntryId, double X, double Y) : ICommand;
