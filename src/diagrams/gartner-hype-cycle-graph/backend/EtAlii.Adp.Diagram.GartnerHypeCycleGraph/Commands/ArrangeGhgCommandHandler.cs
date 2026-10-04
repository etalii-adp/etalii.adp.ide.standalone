using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Arranges a graph: computes the rows and writes each changed one, bottom-up so a row key added to
/// one entry cannot move the lines of the next.
/// </summary>
public sealed class ArrangeGhgCommandHandler(IGhgDocumentStore documents) : ICommandHandler<ArrangeGhgCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(ArrangeGhgCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            var rows = GhgArrangement.RowsOf(model);
            if (rows.Count == 0)
            {
                return GhgEdit.Refused("There is nothing to arrange until this graph has a trend.");
            }

            var changes = new List<(int Start, Func<GhgEdit> Write)>();
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var trend in model.Trends)
            {
                if (rows.TryGetValue(trend.Id, out var row) && row != trend.Row && written.Add(trend.Id))
                {
                    changes.Add((trend.Range.Start, () => GhgWriter.SetRow(document, trend, row)));
                }
            }

            foreach (var trigger in model.Triggers)
            {
                if (rows.TryGetValue(trigger.Id, out var row) && row != trigger.Row && written.Add(trigger.Id))
                {
                    changes.Add((trigger.Range.Start, () => GhgWriter.SetRow(document, trigger, row)));
                }
            }

            foreach (var note in model.Notes)
            {
                if (rows.TryGetValue(note.Id, out var row) && row != note.Row && written.Add(note.Id))
                {
                    changes.Add((note.Range.Start, () => GhgWriter.SetRow(document, note, row)));
                }
            }

            if (changes.Count == 0)
            {
                return GhgEdit.Refused("This graph is already arranged.");
            }

            foreach ((_, var write) in changes.OrderByDescending(change => change.Start))
            {
                write();
            }

            return GhgEdit.Applied;
        });
    }
}
