using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Carries out an end change: one change of <c>end</c>. Giving a moment an end writes the key where the
/// period rule's <c>keys</c> put it, after <c>begin</c>; removing it removes the key. Never a retype.
/// </summary>
public sealed class SetTimelineEndCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<SetTimelineEndCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(TimelineBody body, TimelineModel model, SetTimelineEndCommand command)
    {
        var element = TimelineEdits.ElementOf(model, command.ElementId);
        if (element is null)
        {
            return EditOutcome.Refused(TimelineEdits.ElementGone);
        }

        if (command.End is not null && element.Begin.IsReadable)
        {
            var end = TimelineInstants.Parse(command.End);
            if (end is null)
            {
                return EditOutcome.Refused($"'{command.End}' is not a time this timeline can read.");
            }

            if (end < element.Begin.Value)
            {
                // The third path Requirement 3.4 closes, alongside the adorner and the grid.
                return EditOutcome.Refused("An element cannot end before it begins.");
            }
        }

        if (command.End is null && element.End is null)
        {
            // A moment has no end to remove. An end key holding a mapping or a sequence is not an end
            // (finding B11): the entry is a moment, and the key is kept as it was written.
            return EditOutcome.Applied;
        }

        return TimelineEdits.Set(body, element, ("end", command.End));
    }
}
