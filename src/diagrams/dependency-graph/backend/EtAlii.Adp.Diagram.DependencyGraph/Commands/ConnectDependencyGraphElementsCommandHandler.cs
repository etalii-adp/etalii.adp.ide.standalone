using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Carries out a connect. The inverse is the disconnection of what was connected.</summary>
public sealed class ConnectDependencyGraphElementsCommandHandler : ICommandHandler<ConnectDependencyGraphElementsCommand>
{
    private readonly IDependencyGraphDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public ConnectDependencyGraphElementsCommandHandler(IDependencyGraphDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(ConnectDependencyGraphElementsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(command.From, command.To, StringComparison.Ordinal))
        {
            // A node depending on itself says nothing about what needs what. Refused with the
            // reason rather than silently dropped.
            return Task.FromResult(CommandResult.Failure("A node cannot depend on itself."));
        }

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This graph does not parse, so nothing can be connected until the file is fixed."));
        }

        if (DependencyGraphEdits.ElementOf(entry.Model, command.From) is null ||
            DependencyGraphEdits.ElementOf(entry.Model, command.To) is null)
        {
            // A relation gesture that ends on nothing cancels with a reason, and writing a
            // dependency on a node that is not there would manufacture exactly the dangling
            // reference the validator exists to report.
            return Task.FromResult(CommandResult.Failure("Both ends of a dependency must be nodes of this graph."));
        }

        // Whether the insert is about to create the relations: key, so the inverse can take
        // the key back out and the undo return the file byte for byte.
        var hadSection = DependencyGraphWriter.HasRelationsSection(entry.Document);

        DependencyGraphWriter.InsertRelation(
            entry.Document, entry.Model, command.Id, command.From, command.To, command.Label);

        var error = _documents.Save(command.BodyPath);
        return Task.FromResult(error.Length == 0
            ? CommandResult.Success(new DisconnectDependencyGraphRelationCommand(
                command.BodyPath, command.Id, RemoveEmptiedRelationsSection: !hadSection))
            : CommandResult.Failure(error));
    }
}
