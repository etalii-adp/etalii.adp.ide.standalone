using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Sets a link's weight annotation, or clears it.</summary>
public sealed record SetLinkWeightCommand(string BodyPath, string From, string To, double? Weight) : IDocumentEditCommand;

/// <inheritdoc cref="SetLinkWeightCommand" />
public sealed class SetLinkWeightCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<SetLinkWeightCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, SetLinkWeightCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        return CausalLoopEdits.HasLink(model, command.From, command.To)
            ? CausalLoopEdits.Set(body, CausalLoopEdits.LinkId(command.From, command.To), "CausalLink", ("weight", command.Weight))
            : EditOutcome.Refused(CausalLoopEdits.NoSuchLink);
    }
}
