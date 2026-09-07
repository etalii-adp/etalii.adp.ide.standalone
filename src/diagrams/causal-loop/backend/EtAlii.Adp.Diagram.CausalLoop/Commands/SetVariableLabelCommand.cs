using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>Sets what a reader sees on a variable, without moving what refers to it.</summary>
public sealed record SetVariableLabelCommand(string BodyPath, string Id, string Label) : ICommand;

/// <inheritdoc cref="SetVariableLabelCommand" />
public sealed class SetVariableLabelCommandHandler(ICausalLoopDocumentStore documents)
    : ICommandHandler<SetVariableLabelCommand>
{
    public Task<CommandResult> ExecuteAsync(
        SetVariableLabelCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.SetVariableLabel(entry.Document, entry.Model, command.Id, command.Label));
    }
}
