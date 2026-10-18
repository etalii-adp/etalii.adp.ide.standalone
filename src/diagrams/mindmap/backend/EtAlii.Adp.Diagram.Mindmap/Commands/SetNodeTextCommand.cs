using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>Set a node's text, the empty string included (Requirements 7.5, 7.6).</summary>
public sealed record SetNodeTextCommand(string BodyPath, string NodeId, string Text) : ICommand;

internal sealed class SetNodeTextCommandHandler(IMindmapDocumentStore documents)
    : MindmapCommandHandler(documents), ICommandHandler<SetNodeTextCommand>
{
    public async Task<CommandResult> ExecuteAsync(SetNodeTextCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolve(command.BodyPath, command.NodeId, out var document, out var node, out var failure))
        {
            return failure;
        }

        var previous = node.Text;
        document.SetText(node, command.Text);
        var saved = await Documents.SaveAsync(command.BodyPath, document, new MindmapNodeUpdated(command.NodeId), cancellationToken);

        return CommandResult.FromSave(saved, command with { Text = previous });
    }
}
