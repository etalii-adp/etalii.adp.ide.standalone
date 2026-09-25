using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Resizes an element, clamped to its minimums.</summary>
public sealed class SetFdgSizeCommandHandler(IFdgDocumentStore documents) : ICommandHandler<SetFdgSizeCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetFdgSizeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return FdgEdits.Run(documents, command.BodyPath, command, (document, model) =>
            FdgEdits.ElementOf(model, command.ElementId) is { } element
                ? FdgWriter.Resize(document, element, command.Width, command.Height)
                : FdgEdits.Gone());
    }
}
