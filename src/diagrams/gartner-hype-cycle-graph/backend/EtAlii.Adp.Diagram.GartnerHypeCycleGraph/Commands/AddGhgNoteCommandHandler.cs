using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Adds an empty note of <see cref="DefaultWidth"/> by <see cref="DefaultHeight"/> whose top-left
/// is at the start of the step and the top of the row the drop fell in - so the drop point is
/// inside it, which is how the canvas finds it to open its editor.
/// </summary>
public sealed class AddGhgNoteCommandHandler(IGhgDocumentStore documents) : ICommandHandler<AddGhgNoteCommand>
{
    /// <summary>A new note's width, in canvas units.</summary>
    public const double DefaultWidth = 160;

    /// <summary>A new note's height, in canvas units.</summary>
    public const double DefaultHeight = 64;

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(AddGhgNoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var minted = command.NoteId.Length > 0
            ? command
            : command with { NoteId = ShortGuid.NewShortGuid().ToString() };

        return GhgEdits.Run(documents, minted.BodyPath, minted, (document, model) =>
        {
            if (GhgEdits.IdTaken(model, minted.NoteId))
            {
                return GhgEdit.Refused("That id is already used in this graph.");
            }

            var note = new GhgNote(
                minted.NoteId,
                "",
                GhgScale.MonthContaining(minted.X, model.TimeUnit),
                (int)Math.Floor(minted.Y / GhgScale.RowStep),
                DefaultWidth,
                DefaultHeight,
                new LineRange(0, 0));

            return GhgWriter.AddNote(document, model, note);
        });
    }
}
