using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Carries out a placement: locate against current state, refuse with a sentence when that
/// fails, splice through <see cref="DependencyGraphWriter"/>, report the inverse.
/// </summary>
/// <remarks>
/// The timeline's handler also enforced end-before-begin here, because a guard only in the client
/// is a guard a second client walks around. That guard went with the dates; a coordinate has no
/// invalid value to refuse, so this handler's only precondition is that the node still exists.
/// It is still checked against the document as it is now rather than trusted from when the
/// command was made, because undo and redo dispatch the same instance again later.
/// </remarks>
public sealed class SetDependencyGraphPlacementCommandHandler : ICommandHandler<SetDependencyGraphPlacementCommand>
{
    private readonly IDependencyGraphDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public SetDependencyGraphPlacementCommandHandler(IDependencyGraphDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetDependencyGraphPlacementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This graph does not parse, so nothing in it can be edited until the file is fixed."));
        }

        var element = DependencyGraphEdits.ElementOf(entry.Model, command.ElementId);
        if (element is null)
        {
            return Task.FromResult(CommandResult.Failure("That node is no longer in this graph."));
        }

        var inverse = new SetDependencyGraphPlacementCommand(
            command.BodyPath,
            command.ElementId,
            element.X,
            element.Row,
            command.Description);

        // Each edit is a one-for-one line replace within the node's range, so the ranges the
        // parser recorded stay valid between them and no re-parse is needed mid-command.
        DependencyGraphWriter.SetX(entry.Document, element, command.X);
        DependencyGraphWriter.SetRow(entry.Document, element, command.Row);

        var error = _documents.Save(command.BodyPath);
        return Task.FromResult(error.Length == 0
            ? CommandResult.Success(inverse)
            : CommandResult.Failure(error));
    }
}
