using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.SupplyChain;

/// <summary>Removes a node with its flows, a flow, or a group - leaving its members ungrouped.</summary>
public sealed record RemoveSupplyChainEntryCommand(string BodyPath, string EntryId) : ICommand;
