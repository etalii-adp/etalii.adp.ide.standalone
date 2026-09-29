using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Rewrites a Description - prose the client is never sent.</summary>
public sealed class SetGhgDescriptionCommandHandler(IGhgDocumentStore documents) : ICommandHandler<SetGhgDescriptionCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetGhgDescriptionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (GhgEdits.TrendOf(model, command.Id) is { } trend)
            {
                return GhgWriter.SetDescription(document, trend, command.Description);
            }

            if (GhgEdits.TriggerOf(model, command.Id) is { } trigger)
            {
                return GhgWriter.SetDescription(document, trigger, command.Description);
            }

            return GhgEdits.InfluenceOf(model, command.Id) is { } influence
                ? GhgWriter.SetDescription(document, influence, command.Description)
                : GhgEdits.Gone();
        });
    }
}
