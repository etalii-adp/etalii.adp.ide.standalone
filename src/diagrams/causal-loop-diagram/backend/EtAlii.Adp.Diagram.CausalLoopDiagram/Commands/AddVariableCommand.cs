using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Declares a new variable.</summary>
public sealed record AddVariableCommand(string BodyPath, string Id, string Label) : IDocumentEditCommand;

/// <inheritdoc cref="AddVariableCommand" />
public sealed class AddVariableCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<AddVariableCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, AddVariableCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        if (CausalLoopEdits.AdditionRefusal(model, command.Id, command.Label) is { } refusal) return EditOutcome.Refused(refusal);
        return CausalLoopEdits.AddVariable(body, command.Id, command.Label);
    }
}
