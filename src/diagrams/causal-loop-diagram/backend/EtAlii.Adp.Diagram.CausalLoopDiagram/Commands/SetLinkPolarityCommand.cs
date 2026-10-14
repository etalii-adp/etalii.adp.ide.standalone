using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Changes what a link asserts about the direction of its effect.</summary>
public sealed record SetLinkPolarityCommand(string BodyPath, string From, string To, CausalLoopPolarity Polarity) : IDocumentEditCommand;

/// <inheritdoc cref="SetLinkPolarityCommand" />
public sealed class SetLinkPolarityCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<SetLinkPolarityCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, SetLinkPolarityCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        return CausalLoopEdits.HasLink(model, command.From, command.To)
            ? CausalLoopEdits.Set(body, CausalLoopEdits.LinkId(command.From, command.To), "CausalLink", ("polarity", CausalLoopEdits.Stored(command.Polarity)))
            : EditOutcome.Refused(CausalLoopEdits.NoSuchLink);
    }
}
