using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Moves a trend, trigger or note in time and between rows.</summary>
public sealed class SetGhgPlacementCommandHandler(IGhgDocumentStore documents) : ICommandHandler<SetGhgPlacementCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetGhgPlacementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (GhgEdits.TriggerOf(model, command.ElementId) is { } trigger)
            {
                // The canvas sends the top-left; the trigger is placed by its centre.
                var half = GhgScale.TriggerSize / 2;
                return GhgWriter.SetPlacement(
                    document,
                    trigger,
                    GhgScale.NearestMonthAt(command.X + half, model.TimeUnit),
                    GhgScale.RowAtMiddle(command.Y + half));
            }

            if (GhgEdits.NoteOf(model, command.ElementId) is { } note)
            {
                return GhgWriter.SetPlacement(document, note, GhgScale.NearestMonthAt(command.X, model.TimeUnit), GhgScale.RowAtTop(command.Y));
            }

            if (GhgEdits.TrendOf(model, command.ElementId) is not { } trend)
            {
                return GhgEdits.Gone();
            }

            if (!trend.HasSpan)
            {
                return GhgEdits.NoSpan();
            }

            var start = GhgScale.NearestMonthAt(command.X, model.TimeUnit);
            var shift = start - trend.Start!.Value;
            return GhgWriter.SetSpan(
                document,
                trend,
                start,
                trend.Stop!.Value + shift,
                GhgScale.RowAtTop(command.Y),
                GhgPhases.Moved(trend.DraggedEnds, shift));
        });
    }
}
