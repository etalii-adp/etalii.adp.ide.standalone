using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Carries out a relabel. The inverse is the same command carrying the previous label.</summary>
public sealed class RelabelDependencyGraphRelationCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<RelabelDependencyGraphRelationCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DependencyGraphModel model, RelabelDependencyGraphRelationCommand command)
    {
        var relation = DependencyGraphEdits.RelationOf(model, command.RelationId);
        if (relation is null)
        {
            return EditOutcome.Refused("That dependency is no longer in this graph.");
        }

        DependencyGraphWriter.SetRelationLabel(body, relation, command.Label);
        return EditOutcome.UndoneBy(command with { Label = relation.Label });
    }
}
