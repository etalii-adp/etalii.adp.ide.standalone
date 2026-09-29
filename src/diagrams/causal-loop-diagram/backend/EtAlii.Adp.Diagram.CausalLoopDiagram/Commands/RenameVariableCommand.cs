using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Renames a variable, and every link and loop that names it.</summary>
public sealed record RenameVariableCommand(string BodyPath, string Id, string Name) : ICommand;

/// <inheritdoc cref="RenameVariableCommand" />
public sealed class RenameVariableCommandHandler(ICausalLoopDocumentStore documents) : ICommandHandler<RenameVariableCommand>
{
    public Task<CommandResult> ExecuteAsync(RenameVariableCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.RenameVariable(entry.Document, entry.Model, command.Id, command.Name));
    }
}
