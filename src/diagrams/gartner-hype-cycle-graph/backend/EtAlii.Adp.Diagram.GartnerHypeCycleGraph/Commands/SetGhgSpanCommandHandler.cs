using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Changes a trend's span, scaling its dragged boundaries with it, or a trigger's date.</summary>
public sealed class SetGhgSpanCommandHandler(IGhgDocumentStore documents) : ICommandHandler<SetGhgSpanCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetGhgSpanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (GhgEdits.TriggerOf(model, command.TrendId) is { } trigger)
            {
                return GhgScale.ParseMonth(command.Start ?? command.Stop) is { } date
                    ? GhgWriter.SetPlacement(document, trigger, date, trigger.Row)
                    : GhgEdit.Refused($"'{command.Start ?? command.Stop}' is not a date; write it as YYYY-MM, such as 1947-12.");
            }

            if (GhgEdits.TrendOf(model, command.TrendId) is not { } trend)
            {
                return GhgEdits.Gone();
            }

            if (!trend.HasSpan)
            {
                return GhgEdits.NoSpan();
            }

            int? start = command.Start is null ? trend.Start : GhgScale.ParseMonth(command.Start);
            int? stop = command.Stop is null ? trend.Stop : GhgScale.ParseMonth(command.Stop);
            if (start is null || stop is null)
            {
                return GhgEdit.Refused($"'{command.Start ?? command.Stop}' is not a date; write it as YYYY-MM, such as 2007-06.");
            }

            if (stop <= start)
            {
                return GhgEdit.Refused("A trend must stop after it starts, at least one month later.");
            }

            if (GhgEdits.TooShort(stop.Value - start.Value, trend.VisiblePhases) is { } tooShort)
            {
                return tooShort;
            }

            var dragged = GhgPhases.Rescaled(trend.DraggedEnds, trend.Start!.Value, trend.Stop!.Value, start.Value, stop.Value, trend.VisiblePhases);
            return GhgWriter.SetSpan(document, trend, start.Value, stop.Value, trend.Row, dragged);
        });
    }
}
