using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Removes one node and every relation that references it, as one command with one inverse.
/// </summary>
/// <param name="BodyPath">The document to remove from.</param>
/// <param name="ElementId">Which node.</param>
/// <param name="RemoveEmptiedRelationsSection">Whether an emptied <c>relations:</c> key goes too - set by an inverse undoing the insert that created it.</param>
public sealed record RemoveDependencyGraphElementCommand(
    string BodyPath,
    string ElementId,
    bool RemoveEmptiedRelationsSection = false) : ICommand;
