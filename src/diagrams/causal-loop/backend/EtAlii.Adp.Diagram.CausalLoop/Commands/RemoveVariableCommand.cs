

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>Removes a variable, and the links and loops that named it.</summary>
public sealed record RemoveVariableCommand(string BodyPath, string Id) : ICommand;

/// <inheritdoc cref="RemoveVariableCommand" />
public sealed class RemoveVariableCommandHandler(ICausalLoopDocumentStore documents) : ICommandHandler<RemoveVariableCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveVariableCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.RemoveVariable(entry.Document, entry.Model, command.Id));
    }
}
