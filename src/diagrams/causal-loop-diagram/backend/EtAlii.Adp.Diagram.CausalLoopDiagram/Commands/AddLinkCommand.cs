using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>States a causal link between two declared variables.</summary>
public sealed record AddLinkCommand(string BodyPath, string From, string To, CausalLoopPolarity Polarity) : IDocumentEditCommand;

/// <inheritdoc cref="AddLinkCommand" />
public sealed class AddLinkCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<AddLinkCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, AddLinkCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        if (!model.Declares(command.From) || !model.Declares(command.To)) return EditOutcome.Refused(CausalLoopEdits.NoSuchVariable);
        if (CausalLoopEdits.HasLink(model, command.From, command.To)) return EditOutcome.Refused(CausalLoopEdits.LinkTaken);

        // The definition's hook claims every feedback loop the new link closes, in the same undoable
        // edit: a link and the loops it creates are the one gesture the user made.
        return CausalLoopDefinition.Draw(body, command.From, command.To, CausalLoopEdits.Stored(command.Polarity));
    }
}
