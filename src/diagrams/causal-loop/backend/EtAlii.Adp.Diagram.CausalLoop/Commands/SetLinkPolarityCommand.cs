using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>Changes what a link asserts about the direction of its effect.</summary>
public sealed record SetLinkPolarityCommand(string BodyPath, string From, string To, CausalLoopPolarity Polarity) : ICommand;

/// <inheritdoc cref="SetLinkPolarityCommand" />
public sealed class SetLinkPolarityCommandHandler(ICausalLoopDocumentStore documents) : ICommandHandler<SetLinkPolarityCommand>
{
    public Task<CommandResult> ExecuteAsync(SetLinkPolarityCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.SetLinkPolarity(entry.Document, entry.Model, command.From, command.To, command.Polarity));
    }
}
