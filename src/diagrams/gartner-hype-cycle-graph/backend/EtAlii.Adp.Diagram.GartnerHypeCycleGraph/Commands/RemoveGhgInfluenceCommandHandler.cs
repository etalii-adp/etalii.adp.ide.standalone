using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Removes one influence entry.</summary>
public sealed class RemoveGhgInfluenceCommandHandler(IGhgDocumentStore documents) : GhgEditHandler<RemoveGhgInfluenceCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(GhgBody document, GhgModel model, RemoveGhgInfluenceCommand command) =>
        model.InfluenceOf(command.InfluenceId) is { } influence
            ? GhgWriter.RemoveInfluence(document, influence)
            : GhgRefusals.Gone;
}
