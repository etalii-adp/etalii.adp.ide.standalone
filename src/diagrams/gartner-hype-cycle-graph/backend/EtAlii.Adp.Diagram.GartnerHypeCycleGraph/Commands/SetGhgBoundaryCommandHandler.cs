using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Writes one dragged boundary.</summary>
public sealed class SetGhgBoundaryCommandHandler(IGhgDocumentStore documents) : ICommandHandler<SetGhgBoundaryCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetGhgBoundaryCommand command, CancellationToken cancellationToken = default)
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

            if (command.Index < 0 || command.Index >= trend.VisiblePhases - 1)
            {
                return GhgEdit.Refused("Only a boundary between two visible phases can be moved.");
            }

            if (GhgScale.ParseMonth(command.Month) is not { } month)
            {
                return GhgEdit.Refused($"'{command.Month}' is not a date; write it as YYYY-MM, such as 2007-06.");
            }

            var dragged = GhgPhases.WithBoundary(trend.DraggedEnds, command.Index, month, trend.Start!.Value, trend.Stop!.Value, trend.VisiblePhases);
            return GhgWriter.SetBoundaries(document, trend, dragged);
        });
    }
}
