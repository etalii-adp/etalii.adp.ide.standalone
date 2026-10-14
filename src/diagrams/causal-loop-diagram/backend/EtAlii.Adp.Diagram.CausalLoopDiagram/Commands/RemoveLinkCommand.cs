using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Withdraws a link. Loops through it survive: a loop is a claim of its own.</summary>
public sealed record RemoveLinkCommand(string BodyPath, string From, string To) : IDocumentEditCommand;

/// <inheritdoc cref="RemoveLinkCommand" />
public sealed class RemoveLinkCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<RemoveLinkCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, RemoveLinkCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        return CausalLoopEdits.HasLink(model, command.From, command.To)
            ? CausalLoopEdits.Remove(body, CausalLoopEdits.LinkId(command.From, command.To))
            : EditOutcome.Refused(CausalLoopEdits.NoSuchLink);
    }
}
