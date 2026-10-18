using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>Set a node's notes; empty removes them (Requirement 7.7).</summary>
public sealed record SetNodeNotesCommand(string BodyPath, string NodeId, string Notes) : ICommand;

internal sealed class SetNodeNotesCommandHandler(IMindmapDocumentStore documents)
    : MindmapCommandHandler(documents), ICommandHandler<SetNodeNotesCommand>
{
    public async Task<CommandResult> ExecuteAsync(SetNodeNotesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolve(command.BodyPath, command.NodeId, out var document, out var node, out var failure))
        {
            return failure;
        }

        var previous = node.Notes;
        document.SetNotes(node, command.Notes);
        var saved = await Documents.SaveAsync(command.BodyPath, document, new MindmapNodeUpdated(command.NodeId), cancellationToken);

        return CommandResult.FromSave(saved, command with { Notes = previous });
    }
}
