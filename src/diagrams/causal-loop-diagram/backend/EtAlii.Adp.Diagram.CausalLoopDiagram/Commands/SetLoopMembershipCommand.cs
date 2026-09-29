using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Changes the cycle a loop claims.</summary>
public sealed record SetLoopMembershipCommand(string BodyPath, string Identifier, IReadOnlyList<string> Variables) : ICommand;

/// <inheritdoc cref="SetLoopMembershipCommand" />
public sealed class SetLoopMembershipCommandHandler(ICausalLoopDocumentStore documents) : ICommandHandler<SetLoopMembershipCommand>
{
    public Task<CommandResult> ExecuteAsync(SetLoopMembershipCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.SetLoopMembership(entry.Document, entry.Model, command.Identifier, command.Variables));
    }
}
