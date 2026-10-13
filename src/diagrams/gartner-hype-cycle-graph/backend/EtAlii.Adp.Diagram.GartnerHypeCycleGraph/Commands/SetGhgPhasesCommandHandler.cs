using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Rewrites a trend's phase count.</summary>
public sealed class SetGhgPhasesCommandHandler(IGhgDocumentStore documents) : GhgEditHandler<SetGhgPhasesCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(GhgBody document, GhgModel model, SetGhgPhasesCommand command)
    {
        if (model.TrendOf(command.TrendId) is not { } trend)
        {
            return GhgRefusals.Gone;
        }

        if (trend.HasSpan && GhgRefusals.TooShort(trend.Months, command.Phases) is { } tooShort)
        {
            return tooShort;
        }

        return GhgWriter.SetPhases(document, trend, command.Phases);
    }
}
