using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Puts one line back exactly as it was. What an edit's inverse restores.
/// </summary>
/// <remarks>
/// <para>
/// This exists because restoring the <em>values</em> is not the same as restoring the
/// <em>file</em>. A component written `[0.90, 0.10]` and moved away comes back as `[0.9, 0.1]`
/// if the inverse recomputes from numbers - the same position, a different byte sequence. A
/// user who drags by accident and immediately undoes would be left with a modified file, which
/// is precisely the diff Requirement 3.1 exists to prevent, in the one case where they asked
/// for nothing to have happened.
/// </para>
/// <para>
/// The same shape `c4-diagrams` uses for <c>RestoreC4ElementPositionCommand</c>: an edit and
/// its undo are different commands, because they are different operations.
/// </para>
/// </remarks>
/// <param name="BodyPath">The `.owm` file to edit.</param>
/// <param name="Line">The 1-based line to put back.</param>
/// <param name="Text">The line's text, exactly as it was.</param>
public sealed record RestoreWardleyLineCommand(string BodyPath, uint Line, string Text) : IDocumentEditCommand;

/// <summary>Restores one line verbatim, and names the command that puts back what it replaced.</summary>
public sealed class RestoreWardleyLineCommandHandler(IWardleyDocumentStore documents) : WardleyEditHandler<RestoreWardleyLineCommand>(documents)
{
    protected override EditOutcome Apply(WardleyDocument body, WardleyMap model, RestoreWardleyLineCommand command)
    {
        if (command.Line < 1 || command.Line > body.Lines.Count)
        {
            // The document has changed shape underneath this - a redo after an edit that
            // removed lines, say. Refusing is the honest answer (Requirement 9.4).
            return EditOutcome.Refused("That line is no longer in this map.");
        }

        var replaced = body.Lines[(int)command.Line - 1];
        body.ReplaceLine(command.Line, command.Text);
        return EditOutcome.UndoneBy(command with { Text = replaced });
    }
}
