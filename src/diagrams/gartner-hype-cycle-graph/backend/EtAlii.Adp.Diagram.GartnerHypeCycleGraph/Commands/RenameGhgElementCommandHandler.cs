using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Rewrites a trend's or trigger's name, or a note's text.</summary>
public sealed class RenameGhgElementCommandHandler(IGhgDocumentStore documents) : GhgEditHandler<RenameGhgElementCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(GhgBody document, GhgModel model, RenameGhgElementCommand command)
    {
        if (model.TrendOf(command.ElementId) is { } trend)
        {
            return GhgWriter.SetName(document, trend, command.Name);
        }

        if (model.TriggerOf(command.ElementId) is { } trigger)
        {
            return GhgWriter.SetName(document, trigger, command.Name);
        }

        return model.NoteOf(command.ElementId) is { } note
            ? GhgWriter.SetText(document, note, command.Name)
            : GhgRefusals.Gone;
    }
}
