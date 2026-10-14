using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Removes a variable, and the links and loops that named it.</summary>
public sealed record RemoveVariableCommand(string BodyPath, string Id) : IDocumentEditCommand;

/// <inheritdoc cref="RemoveVariableCommand" />
public sealed class RemoveVariableCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<RemoveVariableCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, RemoveVariableCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        if (!model.Declares(command.Id)) return EditOutcome.Refused(CausalLoopEdits.NoSuchVariable);

        // The binding's removal cascades to the links and loops that name the variable: a link to a removed
        // variable would dangle, and a loop through it would name a path the arrows no longer form.
        return CausalLoopEdits.Remove(body, CausalLoopEdits.VariableId(command.Id));
    }
}
