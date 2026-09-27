using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Carries out a placement: locate against current state, refuse with a sentence when that
/// fails, splice through <see cref="TimelineWriter"/>, report the inverse.
/// </summary>
/// <remarks>
/// <para>
/// The end-before-begin clamp lives here as well as in the canvas, and this copy is the one that
/// makes Requirement 3.4 true: the canvas clamp is what the user feels, but a guard only in the
/// client is a guard a second client walks around. With this and the grid's refusal (Requirement
/// 10.3), the only way a file contains an inverted element is that somebody edited the text by
/// hand.
/// </para>
/// <para>
/// Preconditions are checked against the document as it is now rather than trusted from when the
/// command was made, because undo and redo dispatch the same instance again later.
/// </para>
/// </remarks>
public sealed class SetTimelinePlacementCommandHandler : ICommandHandler<SetTimelinePlacementCommand>
{
    private readonly ITimelineDocumentStore _documents;

    /// <summary>Creates the handler over the one store that owns the documents.</summary>
    public SetTimelinePlacementCommandHandler(ITimelineDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetTimelinePlacementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This timeline does not parse, so nothing in it can be edited until the file is fixed."));
        }

        var element = entry.Model.Elements.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, command.ElementId, StringComparison.Ordinal));
        if (element is null)
        {
            return Task.FromResult(CommandResult.Failure("That element is no longer in this timeline."));
        }

        if (InvertsTheElement(command))
        {
            // Requirement 3.4: no path through the application may create end-before-begin.
            return Task.FromResult(CommandResult.Failure("An element cannot end before it begins."));
        }

        var inverse = new SetTimelinePlacementCommand(
            command.BodyPath,
            command.ElementId,
            element.Begin.Text,
            element.End?.Text,
            element.Row,
            command.Description);

        // Each edit is a one-for-one line replace within the element's range, so the ranges the
        // parser recorded stay valid between them and no re-parse is needed mid-command. SetEnd
        // is skipped for a moment: a placement never gives an element an end it did not have -
        // that is the context menu's action, not a drag's side effect (Requirement 7.5).
        TimelineWriter.SetBegin(entry.Document, element, command.Begin);
        if (command.End is not null && element.End is not null)
        {
            TimelineWriter.SetEnd(entry.Document, element, command.End);
        }

        TimelineWriter.SetRow(entry.Document, element, command.Row);

        var saved = _documents.Save(command.BodyPath, entry);
        return Task.FromResult(!saved.Failed
            ? CommandResult.Success(inverse)
            : CommandResult.Failure(saved.Error));
    }

    /// <summary>
    /// Whether the command's own values put its end before its begin. Judged from the command,
    /// not the document: the document's current state may already be inverted from a hand edit,
    /// and this command may be the one that fixes it.
    /// </summary>
    private static bool InvertsTheElement(SetTimelinePlacementCommand command)
    {
        if (command.End is null)
        {
            return false;
        }

        var begin = TimelineInstants.Parse(command.Begin);
        var end = TimelineInstants.Parse(command.End);

        // A value that will not parse cannot be judged; the rules report it, and refusing an
        // edit for a reason that is not true would block the user from fixing their file.
        return begin is not null && end is not null && end < begin;
    }
}
