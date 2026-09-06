using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Carries out a relabel. The inverse is the same command carrying the previous label.</summary>
public sealed class RelabelDependencyGraphRelationCommandHandler : ICommandHandler<RelabelDependencyGraphRelationCommand>
{
    private readonly IDependencyGraphDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public RelabelDependencyGraphRelationCommandHandler(IDependencyGraphDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RelabelDependencyGraphRelationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This graph does not parse, so nothing can be relabelled until the file is fixed."));
        }

        var relation = DependencyGraphEdits.RelationOf(entry.Model, command.RelationId);
        if (relation is null)
        {
            return Task.FromResult(CommandResult.Failure("That dependency is no longer in this graph."));
        }

        var inverse = new RelabelDependencyGraphRelationCommand(command.BodyPath, command.RelationId, relation.Label);
        DependencyGraphWriter.SetRelationLabel(entry.Document, relation, command.Label);

        var error = _documents.Save(command.BodyPath);
        return Task.FromResult(error.Length == 0
            ? CommandResult.Success(inverse)
            : CommandResult.Failure(error));
    }
}
