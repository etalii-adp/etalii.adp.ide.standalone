using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Gives every trend, trigger and note the row <see cref="GhgArrangement"/> chooses, as one undo.</summary>
/// <param name="BodyPath">The graph.</param>
public sealed record ArrangeGhgCommand(string BodyPath) : ICommand;
