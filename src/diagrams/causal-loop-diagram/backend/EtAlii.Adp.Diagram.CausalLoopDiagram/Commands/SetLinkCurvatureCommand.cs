using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>Bows a link's arc to the other side of its chord, or puts it back.</summary>
public sealed record SetLinkCurvatureCommand(string BodyPath, string From, string To, bool Flipped) : ICommand;

/// <inheritdoc cref="SetLinkCurvatureCommand" />
public sealed class SetLinkCurvatureCommandHandler(ICausalLoopDocumentStore documents) : ICommandHandler<SetLinkCurvatureCommand>
{
    public Task<CommandResult> ExecuteAsync(SetLinkCurvatureCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return CausalLoopEdits.Run(documents, command.BodyPath, command, entry =>
            CausalLoopWriter.SetLinkCurvature(entry.Document, entry.Model, command.From, command.To, command.Flipped));
    }
}
