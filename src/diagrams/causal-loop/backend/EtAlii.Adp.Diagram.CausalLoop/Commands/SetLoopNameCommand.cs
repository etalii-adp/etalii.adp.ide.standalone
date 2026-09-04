using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>Renames a loop, without touching what it runs through.</summary>
public sealed record SetLoopNameCommand(string BodyPath, string Identifier, string Name) : ICommand;

/// <inheritdoc cref="SetLoopNameCommand" />
public sealed class SetLoopNameCommandHandler(ICausalLoopDocumentStore documents) : ICommandHandler<SetLoopNameCommand>
{
    public Task<CommandResult> ExecuteAsync(SetLoopNameCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.SetLoopName(entry.Document, entry.Model, command.Identifier, command.Name));
    }
}
