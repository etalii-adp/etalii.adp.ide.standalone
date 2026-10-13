using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Arranges a timeline: computes the rows, and writes them as <see cref="SetTimelineRowsCommandHandler"/> does.</summary>
public sealed class ArrangeTimelineCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<ArrangeTimelineCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, TimelineModel model, ArrangeTimelineCommand command)
    {
        var rows = TimelineArrangement.RowsOf(model);
        return rows.Count == 0
            ? EditOutcome.Refused("There is nothing to arrange until this timeline has an element.")
            : SetTimelineRowsCommandHandler.Write(body, model, command.BodyPath, rows);
    }
}

/// <summary>Writes each element's row, bottom-up, and answers with the rows they had.</summary>
public sealed class SetTimelineRowsCommandHandler(ITimelineDocumentStore documents)
    : TimelineEditHandler<SetTimelineRowsCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(LineDocument body, TimelineModel model, SetTimelineRowsCommand command) =>
        Write(body, model, command.BodyPath, command.Rows);

    /// <summary>The write both commands share; the inverse names only the elements whose row changed.</summary>
    internal static EditOutcome Write(LineDocument body, TimelineModel model, string bodyPath, IReadOnlyDictionary<string, int> rows)
    {
        var before = new Dictionary<string, int>(StringComparer.Ordinal);

        // Bottom-up, so a row key added to one element cannot move the lines of the next one written.
        foreach (var element in model.Elements.OrderByDescending(element => element.Range.Start))
        {
            if (!rows.TryGetValue(element.Id, out var row) || row == element.Row || before.ContainsKey(element.Id))
            {
                continue;
            }

            before[element.Id] = element.Row;
            TimelineWriter.SetRow(body, element, row);
        }

        return before.Count == 0
            ? EditOutcome.Refused("This timeline is already arranged.")
            : EditOutcome.UndoneBy(new SetTimelineRowsCommand(bodyPath, before));
    }
}
