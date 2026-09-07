

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>Declares a new variable.</summary>
public sealed record AddVariableCommand(string BodyPath, string Id, string Label) : ICommand;

/// <inheritdoc cref="AddVariableCommand" />
public sealed class AddVariableCommandHandler(ICausalLoopDocumentStore documents) : ICommandHandler<AddVariableCommand>
{
    public Task<CommandResult> ExecuteAsync(AddVariableCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.AddVariable(entry.Document, entry.Model, command.Id, command.Label));
    }
}
