using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Sets a Name, or a Comment's text: whichever the element says.</summary>
public sealed class RenameFdgElementCommandHandler(IFdgDocumentStore documents) : ICommandHandler<RenameFdgElementCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RenameFdgElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return FdgEdits.Run(documents, command.BodyPath, command, (document, model) =>
            FdgEdits.ElementOf(model, command.ElementId) switch
            {
                null => FdgEdits.Gone(),
                { IsComment: true } comment => FdgWriter.SetText(document, comment, command.Value),
                var element => FdgWriter.SetName(document, element, command.Value),
            });
    }
}
