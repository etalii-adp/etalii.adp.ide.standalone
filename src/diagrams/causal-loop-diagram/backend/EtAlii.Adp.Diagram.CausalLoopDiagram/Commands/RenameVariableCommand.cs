using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Renames a variable, and every link and loop that names it.</summary>
public sealed record RenameVariableCommand(string BodyPath, string Id, string Name) : IDocumentEditCommand;

/// <inheritdoc cref="RenameVariableCommand" />
public sealed class RenameVariableCommandHandler(ICausalLoopDocumentStore documents) : CausalLoopEditHandler<RenameVariableCommand>(documents)
{
    /// <inheritdoc />
    protected override EditOutcome Apply(CausalLoopBody body, CausalLoopModel model, RenameVariableCommand command)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        if (!CausalLoopEdits.IsUsableName(command.Name)) return EditOutcome.Refused(CausalLoopEdits.UnusableName);
        if (!model.Declares(command.Id)) return EditOutcome.Refused(CausalLoopEdits.NoSuchVariable);
        return model.Declares(command.Name)
            ? EditOutcome.Refused(CausalLoopEdits.AlreadyDeclared)
            // The binding carries the name into every link and loop that refers to it, in the same edit: a
            // rename that left them behind would turn every link through the variable into a dangling one.
            : CausalLoopEdits.Set(body, CausalLoopEdits.VariableId(command.Id), "Variable", ("name", command.Name));
    }
}
