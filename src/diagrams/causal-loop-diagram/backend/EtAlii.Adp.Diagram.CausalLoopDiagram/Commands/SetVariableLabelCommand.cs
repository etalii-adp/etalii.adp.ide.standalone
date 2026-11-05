using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Sets what a reader sees on a variable, without moving what refers to it.</summary>
public sealed record SetVariableLabelCommand(string BodyPath, string Id, string Label) : IDocumentEditCommand;

/// <inheritdoc cref="SetVariableLabelCommand" />
/// <remarks>
/// The label and the identifier are edited separately on purpose. The identifier is what links and loops
/// refer to, so changing it moves references (<see cref="RenameVariableCommand"/>); the label is prose
/// nothing refers to, so changing it is a one-word edit that can hold spaces and punctuation a name cannot.
/// </remarks>
public sealed class SetVariableLabelCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<SetVariableLabelCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, SetVariableLabelCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        if (!model.Declares(command.Id)) return EditOutcome.Refused(CausalLoopEdits.NoSuchVariable);
        return !CausalLoopEdits.IsUsableText(command.Label)
            ? EditOutcome.Refused(CausalLoopEdits.UnusableText)
            : CausalLoopEdits.Set(body, CausalLoopEdits.VariableId(command.Id), "Variable", ("label", CausalLoopEdits.Text(command.Label)));
    }
}
