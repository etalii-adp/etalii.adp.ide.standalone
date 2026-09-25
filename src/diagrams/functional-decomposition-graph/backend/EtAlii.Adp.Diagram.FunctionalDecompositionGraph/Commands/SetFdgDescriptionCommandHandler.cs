using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Sets the Description of an element or a connection.</summary>
public sealed class SetFdgDescriptionCommandHandler(IFdgDocumentStore documents) : ICommandHandler<SetFdgDescriptionCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetFdgDescriptionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return FdgEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (FdgEdits.ElementOf(model, command.Id) is { } element)
            {
                return FdgWriter.SetDescription(document, element, command.Description);
            }

            return FdgEdits.ConnectionOf(model, command.Id) is { } connection
                ? FdgWriter.SetConnectionDescription(document, connection, command.Description)
                : FdgEdit.Refused("That is no longer in this graph.");
        });
    }
}
