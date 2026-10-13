using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>Rewrites one end of an influence.</summary>
public sealed class SetGhgAttachmentCommandHandler(IGhgDocumentStore documents) : GhgEditHandler<SetGhgAttachmentCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(GhgBody document, GhgModel model, SetGhgAttachmentCommand command) =>
        model.InfluenceOf(command.InfluenceId) is { } influence
            ? GhgWriter.SetEnd(document, influence, command.Side, command.End)
            : GhgRefusals.Gone;
}
