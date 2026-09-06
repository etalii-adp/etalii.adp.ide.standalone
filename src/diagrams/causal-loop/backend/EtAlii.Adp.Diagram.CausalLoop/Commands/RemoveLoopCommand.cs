using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>Withdraws a loop's claim - and nothing else. The links it named survive.</summary>
public sealed record RemoveLoopCommand(string BodyPath, string Identifier) : ICommand;

/// <inheritdoc cref="RemoveLoopCommand" />
public sealed class RemoveLoopCommandHandler(ICausalLoopDocumentStore documents) : ICommandHandler<RemoveLoopCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveLoopCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.RemoveLoop(entry.Document, entry.Model, command.Identifier));
    }
}
