using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Removes the three boundary keys.</summary>
public sealed class ClearGhgBoundariesCommandHandler(IGhgDocumentStore documents) : GhgEditHandler<ClearGhgBoundariesCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(GhgBody document, GhgModel model, ClearGhgBoundariesCommand command)
    {
        if (model.TrendOf(command.TrendId) is not { } trend)
        {
            return GhgRefusals.Gone;
        }

        // The definition's evenPhases: refused with its unavailable reason, else every boundary unset.
        return GhgDefinition.Apply(document, OperationInterpreter.Run(
            GhgDefinition.Specification,
            "evenPhases",
            document.Disl.Diagram,
            GhgDefinition.ElementOf(document.Disl.Diagram, trend.Id),
            DislIds.Fixed(),
            env: GhgDefinition.EditEnv));
    }
}
