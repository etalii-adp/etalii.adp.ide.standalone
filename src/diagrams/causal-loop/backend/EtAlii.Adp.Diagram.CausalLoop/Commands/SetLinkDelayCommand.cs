using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>Marks a link's effect as delayed, or unmarks it.</summary>
public sealed record SetLinkDelayCommand(string BodyPath, string From, string To, bool Delayed) : ICommand;

/// <inheritdoc cref="SetLinkDelayCommand" />
public sealed class SetLinkDelayCommandHandler(ICausalLoopDocumentStore documents) : ICommandHandler<SetLinkDelayCommand>
{
    public Task<CommandResult> ExecuteAsync(SetLinkDelayCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.SetLinkDelay(entry.Document, entry.Model, command.From, command.To, command.Delayed));
    }
}
