

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Carries out an add. The inverse is the removal of what was added.</summary>
public sealed class AddDependencyGraphElementCommandHandler : ICommandHandler<AddDependencyGraphElementCommand>
{
    private readonly IDependencyGraphDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public AddDependencyGraphElementCommandHandler(IDependencyGraphDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(AddDependencyGraphElementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.Id))
        {
            return Task.FromResult(CommandResult.Failure("A node needs an id."));
        }

        // The timeline refused an element with no begin and one born ending before it began.
        // Neither has a counterpart here: every coordinate is a position, including zero and
        // including a negative one, so there is nothing left to refuse but a missing id.
        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This graph does not parse, so nothing can be added until the file is fixed."));
        }

        if (DependencyGraphEdits.ElementOf(entry.Model, command.Id) is not null)
        {
            // Redo lands here when the node survived - re-adding it would duplicate the id.
            return Task.FromResult(CommandResult.Failure("A node with that id is already in this graph."));
        }

        DependencyGraphWriter.InsertElement(
            entry.Document, entry.Model, command.Id, command.Label, command.X, command.Row);

        var error = _documents.Save(command.BodyPath);
        return Task.FromResult(error.Length == 0
            ? CommandResult.Success(new RemoveDependencyGraphElementCommand(command.BodyPath, command.Id))
            : CommandResult.Failure(error));
    }
}
