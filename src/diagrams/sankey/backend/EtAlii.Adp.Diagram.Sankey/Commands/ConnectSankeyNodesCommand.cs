using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Adds a flow from <paramref name="From"/> to <paramref name="To"/>.</summary>
public sealed record ConnectSankeyNodesCommand(string BodyPath, string From, string To) : ICommand;
