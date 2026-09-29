using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Rewrites one end of an influence.</summary>
public sealed class SetGhgAttachmentCommandHandler(IGhgDocumentStore documents) : ICommandHandler<SetGhgAttachmentCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetGhgAttachmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
            GhgEdits.InfluenceOf(model, command.InfluenceId) is { } influence
                ? GhgWriter.SetEnd(document, influence, command.Side, command.End)
                : GhgEdits.Gone());
    }
}
