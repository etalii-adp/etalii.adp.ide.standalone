using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Restates what a loop claims about its own polarity.</summary>
public sealed record SetLoopIdentifierCommand(string BodyPath, string Identifier, string Replacement) : IDocumentEditCommand;

/// <inheritdoc cref="SetLoopIdentifierCommand" />
/// <remarks>
/// The identifier is the author's claim about the loop's polarity, so this is how an author accepts the
/// arithmetic when the two disagree - or insists on their own reading. The disagreement is never resolved
/// for them; this only lets them resolve it.
/// </remarks>
public sealed class SetLoopIdentifierCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<SetLoopIdentifierCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, SetLoopIdentifierCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        if (!CausalLoopEdits.IsUsableName(command.Replacement)) return EditOutcome.Refused(CausalLoopEdits.UnusableName);
        if (command.Replacement != command.Identifier && CausalLoopEdits.HasLoop(model, command.Replacement)) return EditOutcome.Refused(CausalLoopEdits.LoopTaken);
        return CausalLoopEdits.HasLoop(model, command.Identifier)
            ? CausalLoopEdits.Set(body, CausalLoopEdits.LoopId(command.Identifier), "Loop", ("identifier", command.Replacement))
            : EditOutcome.Refused(CausalLoopEdits.NoSuchLoop);
    }
}
