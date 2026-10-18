using EtAlii.Adp.History;

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
    public async Task<CommandResult> ExecuteAsync(SetNodeLinkCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var resolution = await ResolveAsync(command.BodyPath, command.NodeId, cancellationToken);
        if (resolution.Failure is { } failure)
        {
            return failure;
        }

        var document = resolution.Document;
        var node = resolution.Node;

        var previous = node.Link;
        document.SetLink(node, command.Link);
        var saved = await Documents.SaveAsync(command.BodyPath, document, new MindmapNodeUpdated(command.NodeId), cancellationToken);

        // The inverse restores the previous link, "no link" included (Requirement 12.6).
        return CommandResult.FromSave(saved, command with { Link = previous });
    }
}
