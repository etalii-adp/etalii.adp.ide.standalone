using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Rewrites a Description - prose the client is never sent.</summary>
public sealed class SetGhgDescriptionCommandHandler(IGhgDocumentStore documents) : GhgEditHandler<SetGhgDescriptionCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(GhgBody document, GhgModel model, SetGhgDescriptionCommand command)
    {
        if (model.TrendOf(command.Id) is { } trend)
        {
            return GhgWriter.SetDescription(document, trend, command.Description);
        }

        if (model.TriggerOf(command.Id) is { } trigger)
        {
            return GhgWriter.SetDescription(document, trigger, command.Description);
        }

        return model.InfluenceOf(command.Id) is { } influence
            ? GhgWriter.SetDescription(document, influence, command.Description)
            : GhgRefusals.Gone;
    }
}
