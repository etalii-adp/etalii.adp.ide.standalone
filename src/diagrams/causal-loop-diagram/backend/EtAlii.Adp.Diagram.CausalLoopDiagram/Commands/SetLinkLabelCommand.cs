using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Sets the note drawn on a link.</summary>
public sealed record SetLinkLabelCommand(string BodyPath, string From, string To, string Label) : ICommand;

/// <inheritdoc cref="SetLinkLabelCommand" />
public sealed class SetLinkLabelCommandHandler(ICausalLoopDocumentStore documents) : ICommandHandler<SetLinkLabelCommand>
{
    public Task<CommandResult> ExecuteAsync(SetLinkLabelCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.SetLinkLabel(entry.Document, entry.Model, command.From, command.To, command.Label));
    }
}
