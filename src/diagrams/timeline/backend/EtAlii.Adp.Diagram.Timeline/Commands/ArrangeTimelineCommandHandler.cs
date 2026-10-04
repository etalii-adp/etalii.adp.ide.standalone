using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>Arranges a timeline: computes the rows, and writes them as <see cref="SetTimelineRowsCommandHandler"/> does.</summary>
public sealed class ArrangeTimelineCommandHandler(ITimelineDocumentStore documents) : ICommandHandler<ArrangeTimelineCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(ArrangeTimelineCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return Task.FromResult(CommandResult.Failure(
                "This timeline does not parse, so it cannot be arranged until the file is fixed."));
        }

        var rows = TimelineArrangement.RowsOf(entry.Model);
        return rows.Count == 0
            ? Task.FromResult(CommandResult.Failure("There is nothing to arrange until this timeline has an element."))
            : Task.FromResult(SetTimelineRowsCommandHandler.Write(documents, command.BodyPath, rows));
    }
}

/// <summary>Writes each element's row, bottom-up, and answers with the rows they had.</summary>
public sealed class SetTimelineRowsCommandHandler(ITimelineDocumentStore documents) : ICommandHandler<SetTimelineRowsCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetTimelineRowsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Write(documents, command.BodyPath, command.Rows));
    }

    /// <summary>The write both commands share; the inverse names only the elements whose row changed.</summary>
    internal static CommandResult Write(ITimelineDocumentStore documents, string bodyPath, IReadOnlyDictionary<string, int> rows)
    {
        var entry = documents.GetOrLoad(bodyPath);
        if (!entry.IsUsable)
        {
            return CommandResult.Failure("This timeline does not parse, so nothing in it can be edited until the file is fixed.");
        }

        var before = new Dictionary<string, int>(StringComparer.Ordinal);

        // Bottom-up, so a row key added to one element cannot move the lines of the next one written.
        foreach (var element in entry.Model.Elements.OrderByDescending(element => element.Range.Start))
        {
            if (!rows.TryGetValue(element.Id, out var row) || row == element.Row || before.ContainsKey(element.Id))
            {
                continue;
            }

            before[element.Id] = element.Row;
            TimelineWriter.SetRow(entry.Document, element, row);
        }

        if (before.Count == 0)
        {
            return CommandResult.Failure("This timeline is already arranged.");
        }

        var saved = documents.Save(bodyPath, entry);
        return saved.Failed
            ? CommandResult.Failure(saved.Error)
            : CommandResult.Success(new SetTimelineRowsCommand(bodyPath, before));
    }
}
