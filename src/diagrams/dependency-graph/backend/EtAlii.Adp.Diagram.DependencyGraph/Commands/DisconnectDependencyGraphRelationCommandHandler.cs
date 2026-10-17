using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Carries out a disconnect, as the binding removes a dependency. The undo is the host's restore.</summary>
public sealed class DisconnectDependencyGraphRelationCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<DisconnectDependencyGraphRelationCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(DependencyGraphBody body, DislModel model, DisconnectDependencyGraphRelationCommand command) =>
        DependencyGraphEdits.Remove(body, DependencyGraphEdits.Dependency, command.RelationId, "That dependency is no longer in this graph.");
}
