using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Resizes a note, moving its top-left when the left or top border moved.</summary>
public sealed class SetGhgNoteSizeCommandHandler(IGhgDocumentStore documents) : GhgEditHandler<SetGhgNoteSizeCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(GhgBody document, GhgModel model, SetGhgNoteSizeCommand command)
    {
        if (model.NoteOf(command.NoteId) is not { } note)
        {
            return GhgRefusals.Gone;
        }

        if (SetGhgNoteSizeCommand.Parse(command.Size) is not { } size)
        {
            return EditOutcome.Refused($"'{command.Size}' is not a size; write it as width x height, such as 160 x 64.");
        }

        return (size.At ?? note.At) is not { } at
            ? EditOutcome.Refused("This note's position cannot be read, so it cannot be resized until it is fixed in the file.")
            : GhgWriter.SetSize(document, note, at, size.Row ?? note.Row, size.Width, size.Height);
    }
}
