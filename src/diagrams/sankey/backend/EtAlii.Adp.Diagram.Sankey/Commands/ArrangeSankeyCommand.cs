using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Reorders the nodes of every column so their bands cross least (<see cref="SankeyArrangement"/>), as one undo.</summary>
/// <param name="BodyPath">The diagram.</param>
public sealed record ArrangeSankeyCommand(string BodyPath) : ICommand;
