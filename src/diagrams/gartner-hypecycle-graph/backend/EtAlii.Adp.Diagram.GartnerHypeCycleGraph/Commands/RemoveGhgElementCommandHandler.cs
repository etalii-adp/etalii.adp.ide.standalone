using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Removes a trend or trigger with its influences, or a note.</summary>
public sealed class RemoveGhgElementCommandHandler(IGhgDocumentStore documents) : ICommandHandler<RemoveGhgElementCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RemoveGhgElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (GhgEdits.TrendOf(model, command.ElementId) is { } trend)
            {
                return GhgWriter.RemoveTrend(document, model, trend);
            }

            if (GhgEdits.TriggerOf(model, command.ElementId) is { } trigger)
            {
                return GhgWriter.RemoveTrigger(document, model, trigger);
            }

            return GhgEdits.NoteOf(model, command.ElementId) is { } note
                ? GhgWriter.RemoveNote(document, note)
                : GhgEdits.Gone();
        });
    }
}
