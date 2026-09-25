using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Removes an element and its connections in one edit.</summary>
public sealed class RemoveFdgElementCommandHandler(IFdgDocumentStore documents) : ICommandHandler<RemoveFdgElementCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RemoveFdgElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return FdgEdits.Run(documents, command.BodyPath, command, (document, model) =>
            FdgEdits.ElementOf(model, command.ElementId) is { } element
                ? FdgWriter.RemoveElement(document, model, element)
                : FdgEdits.Gone());
    }
}
