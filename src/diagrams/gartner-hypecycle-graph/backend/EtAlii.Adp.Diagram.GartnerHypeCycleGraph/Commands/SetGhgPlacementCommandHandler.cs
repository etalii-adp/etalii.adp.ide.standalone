using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Moves a trend in time and between rows.</summary>
public sealed class SetGhgPlacementCommandHandler(IGhgDocumentStore documents) : ICommandHandler<SetGhgPlacementCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetGhgPlacementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (GhgEdits.TrendOf(model, command.TrendId) is not { } trend)
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
