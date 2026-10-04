using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Removes a node with its flows, or a flow.</summary>
public sealed record RemoveSankeyEntryCommand(string BodyPath, string EntryId) : ICommand;
