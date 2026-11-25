using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// Sets a linked node's <c>adp-link</c>, or removes it, as the fixes of its finding do (FBL 17.6, <c>mindmap.md</c> 3.8,
/// etalii.adp spec 092, T017). Without it the node is an ordinary one, whose text is the name the link cached.
/// </summary>
/// <param name="BodyPath">The document.</param>
/// <param name="NodeId">Which node.</param>
/// <param name="Link">The link's new text, or null to remove it.</param>
public sealed record SetNodeAdpLinkCommand(string BodyPath, string NodeId, string? Link) : ICommand;

internal sealed class SetNodeAdpLinkCommandHandler(IMindmapDocumentStore documents)
    : MindmapCommandHandler(documents), ICommandHandler<SetNodeAdpLinkCommand>
{
    public async Task<CommandResult> ExecuteAsync(SetNodeAdpLinkCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var resolution = await ResolveAsync(command.BodyPath, command.NodeId, cancellationToken);
        if (resolution.Failure is { } failure)
        {
            return failure;
        }

        var node = resolution.Node;
        var previous = node.AdpLink;
        if (command.Link is null) node.RemoveFreeplaneAttribute(MindmapNode.AdpLinkAttribute);
        else node.SetFreeplaneAttribute(MindmapNode.AdpLinkAttribute, command.Link);
        return await SaveAsync(command.BodyPath, resolution.Document, new MindmapNodeUpdated(command.NodeId), command with { Link = previous }, cancellationToken);
    }
}
