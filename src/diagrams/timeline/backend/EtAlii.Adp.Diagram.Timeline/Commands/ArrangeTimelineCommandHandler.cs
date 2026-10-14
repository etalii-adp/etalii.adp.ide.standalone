using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Arranges a timeline: computes the rows, and writes them as <see cref="SetTimelineRowsCommandHandler"/> does.</summary>
public sealed class ArrangeTimelineCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<ArrangeTimelineCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(TimelineBody body, TimelineModel model, ArrangeTimelineCommand command)
    {
        var rows = TimelineArrangement.RowsOf(model);
        return rows.Count == 0
            ? EditOutcome.Refused("There is nothing to arrange until this timeline has an element.")
            : SetTimelineRowsCommandHandler.Write(body, model, rows);
    }
}

/// <summary>Writes each element's row, as one batch of changes planned against one reading.</summary>
public sealed class SetTimelineRowsCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<SetTimelineRowsCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(TimelineBody body, TimelineModel model, SetTimelineRowsCommand command) =>
        Write(body, model, command.Rows);

    /// <summary>
    /// The write both commands share: a <c>Set</c> of <c>row</c> for each element whose row changes, the
    /// last holder of an id written twice first as it always was, planned together as one edit.
    /// </summary>
    internal static EditOutcome Write(TimelineBody body, TimelineModel model, IReadOnlyDictionary<string, int> rows)
    {
        var written = new HashSet<string>(StringComparer.Ordinal);
        var refusal = (string?)null;
        body.Batch(() =>
        {
            foreach (var element in model.Elements.OrderByDescending(element => element.Range.Start))
            {
                if (!rows.TryGetValue(element.Id, out var row) || row == element.Row || !written.Add(element.Id))
                {
                    continue;
                }

                refusal ??= TimelineEdits.Set(body, element, ("row", row)).Refusal;
            }
        });

        if (refusal is not null)
        {
            return EditOutcome.Refused(refusal);
        }

        return written.Count == 0 ? EditOutcome.Refused("This timeline is already arranged.") : EditOutcome.Applied;
    }
}
