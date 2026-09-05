using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>States a causal link between two declared variables.</summary>
public sealed record AddLinkCommand(string BodyPath, string From, string To, CausalLoopPolarity Polarity) : ICommand;

/// <inheritdoc cref="AddLinkCommand" />
public sealed class AddLinkCommandHandler(ICausalLoopDocumentStore documents) : ICommandHandler<AddLinkCommand>
{
    public Task<CommandResult> ExecuteAsync(AddLinkCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.AddLink(entry.Document, entry.Model, command.From, command.To, command.Polarity));
    }
}
