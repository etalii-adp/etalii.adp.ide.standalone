using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Removes the three boundary keys.</summary>
public sealed class ClearGhgBoundariesCommandHandler(IGhgDocumentStore documents) : ICommandHandler<ClearGhgBoundariesCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(ClearGhgBoundariesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (GhgEdits.TrendOf(model, command.TrendId) is not { } trend)
            {
                return GhgEdits.Gone();
            }

            return trend.DraggedEnds.All(boundary => boundary is null)
                ? GhgEdit.Refused("This trend's phases are already even.")
                : GhgWriter.SetBoundaries(document, trend, [null, null, null]);
        });
    }
}
