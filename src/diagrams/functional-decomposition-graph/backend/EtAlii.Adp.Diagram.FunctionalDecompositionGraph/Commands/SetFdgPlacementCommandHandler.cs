using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Moves an element by rewriting its top-left.</summary>
public sealed class SetFdgPlacementCommandHandler(IFdgDocumentStore documents) : ICommandHandler<SetFdgPlacementCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetFdgPlacementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return FdgEdits.Run(documents, command.BodyPath, command, (document, model) =>
            FdgEdits.ElementOf(model, command.ElementId) is { } element
                ? FdgWriter.MoveTo(document, element, command.X, command.Y)
                : FdgEdits.Gone());
    }
}
