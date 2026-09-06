using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Changes what a dependency is called; an empty label removes the key.</summary>
/// <param name="BodyPath">The document.</param>
/// <param name="RelationId">Which dependency.</param>
/// <param name="Label">The new label, or empty for none.</param>
public sealed record RelabelDependencyGraphRelationCommand(
    string BodyPath,
    string RelationId,
    string Label) : ICommand;
