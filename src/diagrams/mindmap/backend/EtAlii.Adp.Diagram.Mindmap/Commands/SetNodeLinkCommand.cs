using EtAlii.Adp.Backend;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// Set a node's link, stored map-relative in the <c>LINK</c> attribute exactly as Freeplane
/// does; null unlinks (Requirements 12.1, 12.6). Conversion from a project-relative path
/// happens before the command is built, in <see cref="MindmapLinks"/>.
/// </summary>
public sealed record SetNodeLinkCommand(string BodyPath, string NodeId, string? Link) : ICommand;

internal sealed class SetNodeLinkCommandHandler(IMindmapDocumentStore documents)
    : MindmapCommandHandler(documents), ICommandHandler<SetNodeLinkCommand>
{
    public Task<CommandResult> ExecuteAsync(SetNodeLinkCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolve(command.BodyPath, command.NodeId, out var document, out var node, out var failure))
        {
            return Task.FromResult(failure);
        }

        var previous = node.Link;
        document.SetLink(node, command.Link);
        Documents.Save(command.BodyPath, new MindmapNodeUpdated(command.NodeId));

        // The inverse restores the previous link, "no link" included (Requirement 12.6).
        return Task.FromResult(CommandResult.Success(command with { Link = previous }));
    }
}
