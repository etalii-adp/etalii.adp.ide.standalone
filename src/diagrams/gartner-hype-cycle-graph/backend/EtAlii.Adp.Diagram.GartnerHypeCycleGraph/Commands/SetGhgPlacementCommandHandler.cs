using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Moves a trend, trigger or note in time and between rows.</summary>
public sealed class SetGhgPlacementCommandHandler(IGhgDocumentStore documents) : GhgEditHandler<SetGhgPlacementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(GhgBody document, GhgModel model, SetGhgPlacementCommand command)
    {
        if (model.TriggerOf(command.ElementId) is { } trigger)
        {
            // The canvas sends the top-left; the trigger is placed by its centre.
            var half = GhgScale.TriggerSize / 2;
            return GhgWriter.SetPlacement(
                document,
                trigger,
                GhgScale.NearestMonthAt(command.X + half, model.TimeUnit),
                GhgScale.RowAtMiddle(command.Y + half));
        }

        if (model.NoteOf(command.ElementId) is { } note)
        {
            return GhgWriter.SetPlacement(document, note, GhgScale.NearestMonthAt(command.X, model.TimeUnit), GhgScale.RowAtTop(command.Y));
        }

        if (model.TrendOf(command.ElementId) is not { } trend)
        {
            return GhgRefusals.Gone;
        }

        if (!trend.HasSpan)
        {
            return GhgRefusals.NoSpan;
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
    }
}
