using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Rewrites a trend's or trigger's name, or a note's text.</summary>
public sealed class RenameGhgElementCommandHandler(IGhgDocumentStore documents) : ICommandHandler<RenameGhgElementCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RenameGhgElementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (GhgEdits.TrendOf(model, command.ElementId) is { } trend)
            {
                return GhgWriter.SetName(document, trend, command.Name);
            }

            if (GhgEdits.TriggerOf(model, command.ElementId) is { } trigger)
            {
                return GhgWriter.SetName(document, trigger, command.Name);
            }

            return GhgEdits.NoteOf(model, command.ElementId) is { } note
                ? GhgWriter.SetText(document, note, command.Name)
                : GhgEdits.Gone();
        });
    }
}
