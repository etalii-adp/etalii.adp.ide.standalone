using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Rewrites a trend's or trigger's tags as one flow sequence line.</summary>
public sealed class SetGhgTagsCommandHandler(IGhgDocumentStore documents) : GhgEditHandler<SetGhgTagsCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(GhgBody document, GhgModel model, SetGhgTagsCommand command)
    {
        if (model.TriggerOf(command.TrendId) is { } trigger)
        {
            return GhgWriter.SetTags(document, trigger, command.Parsed);
        }

        return model.TrendOf(command.TrendId) is { } trend
            ? GhgWriter.SetTags(document, trend, command.Parsed)
            : GhgRefusals.Gone;
    }
}
