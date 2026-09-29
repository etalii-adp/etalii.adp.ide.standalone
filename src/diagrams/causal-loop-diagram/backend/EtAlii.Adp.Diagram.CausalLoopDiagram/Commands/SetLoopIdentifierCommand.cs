using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Restates what a loop claims about its own polarity.</summary>
public sealed record SetLoopIdentifierCommand(string BodyPath, string Identifier, string Replacement) : ICommand;

/// <inheritdoc cref="SetLoopIdentifierCommand" />
public sealed class SetLoopIdentifierCommandHandler(ICausalLoopDocumentStore documents)
    : ICommandHandler<SetLoopIdentifierCommand>
{
    public Task<CommandResult> ExecuteAsync(
        SetLoopIdentifierCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.SetLoopIdentifier(entry.Document, entry.Model, command.Identifier, command.Replacement));
    }
}
