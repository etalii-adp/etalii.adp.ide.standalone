using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Sets the note drawn on a link.</summary>
public sealed record SetLinkLabelCommand(string BodyPath, string From, string To, string Label) : IDocumentEditCommand;

/// <inheritdoc cref="SetLinkLabelCommand" />
public sealed class SetLinkLabelCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<SetLinkLabelCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, SetLinkLabelCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        if (!CausalLoopEdits.IsUsableText(command.Label)) return EditOutcome.Refused(CausalLoopEdits.UnusableText);
        return CausalLoopEdits.HasLink(model, command.From, command.To)
            ? CausalLoopEdits.Set(body, CausalLoopEdits.LinkId(command.From, command.To), "CausalLink", ("note", CausalLoopEdits.Text(command.Label)))
            : EditOutcome.Refused(CausalLoopEdits.NoSuchLink);
    }
}
