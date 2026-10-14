using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Marks a link's effect as delayed, or unmarks it.</summary>
public sealed record SetLinkDelayCommand(string BodyPath, string From, string To, bool Delayed) : IDocumentEditCommand;

/// <inheritdoc cref="SetLinkDelayCommand" />
public sealed class SetLinkDelayCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<SetLinkDelayCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, SetLinkDelayCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        return CausalLoopEdits.HasLink(model, command.From, command.To)
            ? CausalLoopEdits.Set(body, CausalLoopEdits.LinkId(command.From, command.To), "CausalLink", ("delayed", command.Delayed))
            : EditOutcome.Refused(CausalLoopEdits.NoSuchLink);
    }
}
