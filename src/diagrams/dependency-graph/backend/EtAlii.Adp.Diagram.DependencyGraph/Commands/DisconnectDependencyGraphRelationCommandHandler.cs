using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Carries out a disconnect. The inverse is a connect carrying the same id, endpoints and
/// label - sufficient because a relation block is machine-shaped; a node's block, which can hold
/// an author's comments, gets the byte-exact restore instead.
/// </summary>
public sealed class DisconnectDependencyGraphRelationCommandHandler(IDependencyGraphDocumentStore documents)
    : DependencyGraphEditHandler<DisconnectDependencyGraphRelationCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, DependencyGraphModel model, DisconnectDependencyGraphRelationCommand command)
    {
        var relation = DependencyGraphEdits.RelationOf(model, command.RelationId);
        if (relation is null)
        {
            return EditOutcome.Refused("That dependency is no longer in this graph.");
        }

        DependencyGraphWriter.RemoveRelation(body, relation);
        return EditOutcome.Applied;
    }
}
