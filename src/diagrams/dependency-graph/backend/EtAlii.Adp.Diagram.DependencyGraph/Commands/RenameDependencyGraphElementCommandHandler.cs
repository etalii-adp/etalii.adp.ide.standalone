using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>Carries out a rename. The inverse is the same command carrying the previous label.</summary>
public sealed class RenameDependencyGraphElementCommandHandler : ICommandHandler<RenameDependencyGraphElementCommand>
{
    private readonly IDependencyGraphDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public RenameDependencyGraphElementCommandHandler(IDependencyGraphDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RenameDependencyGraphElementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This graph does not parse, so nothing can be renamed until the file is fixed."));
        }

        var element = DependencyGraphEdits.ElementOf(entry.Model, command.ElementId);
        if (element is null)
        {
            return Task.FromResult(CommandResult.Failure("That node is no longer in this graph."));
        }

        var inverse = new RenameDependencyGraphElementCommand(command.BodyPath, command.ElementId, element.Label);
        DependencyGraphWriter.SetLabel(entry.Document, element, command.Label);

        var error = _documents.Save(command.BodyPath);
        return Task.FromResult(error.Length == 0
            ? CommandResult.Success(inverse)
            : CommandResult.Failure(error));
    }
}
