using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

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

            // The definition's evenPhases: refused with its unavailable reason, else every boundary unset.
            return GhgDefinition.Apply(document, OperationInterpreter.Run(
                GhgDefinition.Specification,
                "evenPhases",
                document.Disl.Diagram,
                GhgDefinition.ElementOf(document.Disl.Diagram, trend.Id),
                DislIds.Fixed(),
                env: GhgDefinition.EditEnv));
        });
    }
}
