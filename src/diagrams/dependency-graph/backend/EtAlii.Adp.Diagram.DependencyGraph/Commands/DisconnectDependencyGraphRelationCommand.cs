

using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Removes one dependency.</summary>
/// <param name="BodyPath">The document.</param>
/// <param name="RelationId">Which dependency.</param>
public sealed record DisconnectDependencyGraphRelationCommand(
    string BodyPath,
    string RelationId) : IDocumentEditCommand;
