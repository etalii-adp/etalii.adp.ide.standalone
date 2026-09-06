using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>Withdraws a link. Loops through it survive: a loop is a claim of its own.</summary>
public sealed record RemoveLinkCommand(string BodyPath, string From, string To) : ICommand;

/// <inheritdoc cref="RemoveLinkCommand" />
public sealed class RemoveLinkCommandHandler(ICausalLoopDocumentStore documents) : ICommandHandler<RemoveLinkCommand>
{
    public Task<CommandResult> ExecuteAsync(RemoveLinkCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.RemoveLink(entry.Document, entry.Model, command.From, command.To));
    }
}
