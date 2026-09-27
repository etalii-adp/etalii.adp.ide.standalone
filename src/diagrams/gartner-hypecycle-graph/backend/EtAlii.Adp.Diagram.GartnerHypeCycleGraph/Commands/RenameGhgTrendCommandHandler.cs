using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Rewrites a trend's name.</summary>
public sealed class RenameGhgTrendCommandHandler(IGhgDocumentStore documents) : ICommandHandler<RenameGhgTrendCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RenameGhgTrendCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
            GhgEdits.TrendOf(model, command.TrendId) is { } trend
                ? GhgWriter.SetName(document, trend, command.Name)
                : GhgEdits.Gone());
    }
}
