using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Sets what a flow page starts a variable from, its <c>start</c> or its <c>push</c>, or clears it.</summary>
public sealed record SetVariableFlowCommand(string BodyPath, string Variable, string Attribute, double? Value) : IDocumentEditCommand;

/// <inheritdoc cref="SetVariableFlowCommand" />
public sealed class SetVariableFlowCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<SetVariableFlowCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, SetVariableFlowCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        return model.Declares(command.Variable)
            ? CausalLoopEdits.Set(body, CausalLoopEdits.VariableId(command.Variable), "Variable", (command.Attribute, command.Value))
            : EditOutcome.Refused(CausalLoopEdits.NoSuchVariable);
    }
}
