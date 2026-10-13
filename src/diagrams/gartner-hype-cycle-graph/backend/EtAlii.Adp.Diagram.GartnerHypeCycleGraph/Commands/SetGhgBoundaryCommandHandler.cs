using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Writes one dragged boundary.</summary>
public sealed class SetGhgBoundaryCommandHandler(IGhgDocumentStore documents) : GhgEditHandler<SetGhgBoundaryCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(GhgBody document, GhgModel model, SetGhgBoundaryCommand command)
    {
        if (model.TrendOf(command.TrendId) is not { } trend)
        {
            return GhgRefusals.Gone;
        }

        if (!trend.HasSpan)
        {
            return GhgRefusals.NoSpan;
        }

        if (command.Index < 0 || command.Index >= trend.VisiblePhases - 1)
        {
            return EditOutcome.Refused("Only a boundary between two visible phases can be moved.");
        }

        if (GhgScale.ParseMonth(command.Month) is not { } month)
        {
            return EditOutcome.Refused($"'{command.Month}' is not a date; write it as YYYY-MM, such as 2007-06.");
        }

        var dragged = GhgPhases.WithBoundary(trend.DraggedEnds, command.Index, month, trend.Start!.Value, trend.Stop!.Value, trend.VisiblePhases);
        return GhgWriter.SetBoundaries(document, trend, dragged);
    }
}
