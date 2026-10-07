using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Arranges a graph: computes the rows and writes each changed one, bottom-up so a row key added to
/// one entry cannot move the lines of the next.
/// </summary>
/// <remarks>
/// Each write touches its own entry alone, so they are applied as one batch (<see cref="GhgBody.Batch"/>):
/// the body is read again once, not once per row.
/// </remarks>
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

            var writes = RowWrites(document, model, rows);
            if (writes.Count == 0)
            {
                return GhgEdit.Refused("This graph is already arranged.");
            }

            document.Batch(() =>
            {
                foreach (var write in writes)
                {
                    write();
                }
            });

            return GhgEdit.Applied;
        });
    }

    /// <summary>The writes of every entry whose row <paramref name="rows"/> changes, bottom-up: the last entry in the body first.</summary>
    internal static IReadOnlyList<Func<GhgEdit>> RowWrites(GhgBody document, GhgModel model, IReadOnlyDictionary<string, int> rows)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(rows);

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

        return [.. changes.OrderByDescending(change => change.Start).Select(change => change.Write)];
    }
}
