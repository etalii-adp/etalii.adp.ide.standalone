using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Resizes a note, moving its top-left when the left or top border moved.</summary>
public sealed class SetGhgNoteSizeCommandHandler(IGhgDocumentStore documents) : ICommandHandler<SetGhgNoteSizeCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetGhgNoteSizeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (GhgEdits.NoteOf(model, command.NoteId) is not { } note)
            {
                return GhgEdits.Gone();
            }

            if (SetGhgNoteSizeCommand.Parse(command.Size) is not { } size)
            {
                return GhgEdit.Refused($"'{command.Size}' is not a size; write it as width x height, such as 160 x 64.");
            }

            if ((size.At ?? note.At) is not { } at)
            {
                return GhgEdit.Refused("This note's position cannot be read, so it cannot be resized until it is fixed in the file.");
            }

            return GhgWriter.SetSize(document, note, at, size.Row ?? note.Row, size.Width, size.Height);
        });
    }
}
