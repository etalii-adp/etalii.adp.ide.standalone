

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Removes one dependency.</summary>
/// <param name="BodyPath">The document.</param>
/// <param name="RelationId">Which dependency.</param>
/// <param name="RemoveEmptiedRelationsSection">Whether an emptied <c>relations:</c> key goes too - set by an inverse undoing the insert that created it.</param>
public sealed record DisconnectDependencyGraphRelationCommand(
    string BodyPath,
    string RelationId,
    bool RemoveEmptiedRelationsSection = false) : ICommand;
