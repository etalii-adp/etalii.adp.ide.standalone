

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>Set a node's text, the empty string included (Requirements 7.5, 7.6).</summary>
public sealed record SetNodeTextCommand(string BodyPath, string NodeId, string Text) : ICommand;

internal sealed class SetNodeTextCommandHandler(IMindmapDocumentStore documents)
    : MindmapCommandHandler(documents), ICommandHandler<SetNodeTextCommand>
{
    public Task<CommandResult> ExecuteAsync(SetNodeTextCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolve(command.BodyPath, command.NodeId, out var document, out var node, out var failure))
        {
            return Task.FromResult(failure);
        }

        var previous = node.Text;
        document.SetText(node, command.Text);
        Documents.Save(command.BodyPath, new MindmapNodeUpdated(command.NodeId));

        return Task.FromResult(CommandResult.Success(command with { Text = previous }));
    }
}
