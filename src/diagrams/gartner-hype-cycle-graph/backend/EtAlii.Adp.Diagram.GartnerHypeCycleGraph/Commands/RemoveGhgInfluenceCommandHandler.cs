using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Removes one influence entry.</summary>
public sealed class RemoveGhgInfluenceCommandHandler(IGhgDocumentStore documents) : ICommandHandler<RemoveGhgInfluenceCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(RemoveGhgInfluenceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
            GhgEdits.InfluenceOf(model, command.InfluenceId) is { } influence
                ? GhgWriter.RemoveInfluence(document, influence)
                : GhgEdits.Gone());
    }
}
