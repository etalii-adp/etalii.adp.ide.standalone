using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// Moves a node to its new place in its column: directly before <paramref name="AnchorId"/>, or
/// directly after it when <paramref name="After"/>.
/// </summary>
public sealed record MoveSankeyNodeCommand(string BodyPath, string NodeId, string AnchorId, bool After) : ICommand;
