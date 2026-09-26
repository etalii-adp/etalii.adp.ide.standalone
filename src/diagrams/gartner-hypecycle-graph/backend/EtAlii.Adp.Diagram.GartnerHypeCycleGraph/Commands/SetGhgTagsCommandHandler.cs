using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Rewrites a trend's tags as one flow sequence line.</summary>
public sealed class SetGhgTagsCommandHandler(IGhgDocumentStore documents) : ICommandHandler<SetGhgTagsCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetGhgTagsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
            GhgEdits.TrendOf(model, command.TrendId) is { } trend
                ? GhgWriter.SetTags(document, trend, command.Parsed)
                : GhgEdits.Gone());
    }
}
