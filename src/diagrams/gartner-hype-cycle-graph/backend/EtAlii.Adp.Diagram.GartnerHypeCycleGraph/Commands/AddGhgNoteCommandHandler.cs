using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Adds an empty note of <see cref="DefaultWidth"/> by <see cref="DefaultHeight"/> whose top-left
/// is at the start of the step and the top of the row the drop fell in - so the drop point is
/// inside it, which is how the canvas finds it to open its editor.
/// </summary>
public sealed class AddGhgNoteCommandHandler(IGhgDocumentStore documents) : GhgEditHandler<AddGhgNoteCommand>(documents)
{
    /// <summary>A new note's width, in canvas units.</summary>
    public const double DefaultWidth = 160;

    /// <summary>A new note's height, in canvas units.</summary>
    public const double DefaultHeight = 64;

    /// <inheritdoc />
    protected override CommandResult Execute(AddGhgNoteCommand command)
    {
        return base.Execute(command.NoteId.Length > 0 ? command : command with { NoteId = ShortGuid.NewShortGuid().ToString() });
    }

    /// <inheritdoc />
    protected override EditOutcome Apply(GhgBody document, GhgModel model, AddGhgNoteCommand command)
    {
        if (model.IsTaken(command.NoteId))
        {
            return EditOutcome.Refused("That id is already used in this graph.");
        }

        return GhgDefinition.Apply(document, OperationInterpreter.Run(
            GhgDefinition.Specification,
            "addNoteHere",
            document.Disl.Diagram,
            null,
            DislIds.Fixed(command.NoteId),
            new DislInvocation(GhgDefinition.Position(command.X, command.Y, model.TimeUnit)),
            GhgDefinition.EditEnv));
    }
}
