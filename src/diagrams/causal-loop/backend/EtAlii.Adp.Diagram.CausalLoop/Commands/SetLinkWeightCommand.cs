

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>Sets a link's weight annotation, or clears it.</summary>
public sealed record SetLinkWeightCommand(string BodyPath, string From, string To, double? Weight) : ICommand;

/// <inheritdoc cref="SetLinkWeightCommand" />
public sealed class SetLinkWeightCommandHandler(ICausalLoopDocumentStore documents) : ICommandHandler<SetLinkWeightCommand>
{
    public Task<CommandResult> ExecuteAsync(SetLinkWeightCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.SetLinkWeight(entry.Document, entry.Model, command.From, command.To, command.Weight));
    }
}
