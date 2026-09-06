using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>Claims a feedback loop through the named variables.</summary>
public sealed record AddLoopCommand(string BodyPath, string Identifier, string Name, IReadOnlyList<string> Variables) : ICommand;

/// <inheritdoc cref="AddLoopCommand" />
public sealed class AddLoopCommandHandler(ICausalLoopDocumentStore documents) : ICommandHandler<AddLoopCommand>
{
    public Task<CommandResult> ExecuteAsync(AddLoopCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.AddLoop(entry.Document, entry.Model, command.Identifier, command.Name, command.Variables));
    }
}
