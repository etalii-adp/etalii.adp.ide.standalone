using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Rewrites a trend's phase count.</summary>
public sealed class SetGhgPhasesCommandHandler(IGhgDocumentStore documents) : ICommandHandler<SetGhgPhasesCommand>
{
    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(SetGhgPhasesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return GhgEdits.Run(documents, command.BodyPath, command, (document, model) =>
        {
            if (GhgEdits.TrendOf(model, command.TrendId) is not { } trend)
            {
                return GhgEdits.Gone();
            }

            if (trend.HasSpan && GhgEdits.TooShort(trend.Months, command.Phases) is { } tooShort)
            {
                return tooShort;
            }

            return GhgWriter.SetPhases(document, trend, command.Phases);
        });
    }
}
