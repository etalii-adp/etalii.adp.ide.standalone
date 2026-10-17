using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Carries out a relabel, as the binding sets a value: an empty label takes the key out. The undo is the host's restore.</summary>
public sealed class RelabelDependencyGraphRelationCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<RelabelDependencyGraphRelationCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(DependencyGraphBody body, DislModel model, RelabelDependencyGraphRelationCommand command) =>
        DependencyGraphEdits.Set(
            body,
            DependencyGraphEdits.Dependency,
            command.RelationId,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["label"] = command.Label },
            "That dependency is no longer in this graph.");
}
