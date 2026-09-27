using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Removes a trend with its influences.</summary>
public sealed class RemoveGhgTrendCommandHandler(IGhgDocumentStore documents) : ICommandHandler<RemoveGhgTrendCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RemoveGhgTrendCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
            GhgEdits.TrendOf(model, command.TrendId) is { } trend
                ? GhgWriter.RemoveTrend(document, model, trend)
                : GhgEdits.Gone());
    }
}
