using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Carries out a disconnect. The inverse is a connect carrying the same id, endpoints and
/// label - sufficient because a relation block is machine-shaped; a node's block, which can hold
/// an author's comments, gets the byte-exact restore instead.
/// </summary>
public sealed class DisconnectDependencyGraphRelationCommandHandler : ICommandHandler<DisconnectDependencyGraphRelationCommand>
{
    private readonly IDependencyGraphDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public DisconnectDependencyGraphRelationCommandHandler(IDependencyGraphDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(DisconnectDependencyGraphRelationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This graph does not parse, so nothing can be disconnected until the file is fixed."));
        }

        var relation = DependencyGraphEdits.RelationOf(entry.Model, command.RelationId);
        if (relation is null)
        {
            return Task.FromResult(CommandResult.Failure("That dependency is no longer in this graph."));
        }

        // The endpoints go into the inverse in the order they were written: putting a dependency
        // back the other way round would silently reverse what the graph says.
        var inverse = new ConnectDependencyGraphElementsCommand(
            command.BodyPath, relation.Id, relation.From, relation.To, relation.Label);

        DependencyGraphWriter.RemoveRelation(entry.Document, relation);

        if (command.RemoveEmptiedRelationsSection)
        {
            // The undo of a connect that created the relations: key on demand.
            DependencyGraphWriter.RemoveRelationsSectionIfEmpty(
                entry.Document, DependencyGraphParser.Parse(entry.Document));
        }

        var error = _documents.Save(command.BodyPath);
        return Task.FromResult(error.Length == 0
            ? CommandResult.Success(inverse)
            : CommandResult.Failure(error));
    }
}
