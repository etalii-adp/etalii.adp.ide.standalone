using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>Remove <paramref name="NodeId"/> and everything under it (Requirement 7.4).</summary>
public sealed record RemoveNodeCommand(string BodyPath, string NodeId) : ICommand;

/// <summary>
/// Put a removed subtree back exactly where it was. The inverse of a remove is the one
/// mindmap command that carries state rather than coordinates: restoring a branch needs every
/// node's id, text, notes and link, so the handler captures the detached XML into it.
/// </summary>
/// <param name="Subtree">The subtree as Freeplane XML, held as text so the command stays immutable data.</param>
public sealed record RestoreSubtreeCommand(string BodyPath, string ParentId, int Index, string Subtree) : ICommand;

internal sealed class RemoveNodeCommandHandler(IMindmapDocumentStore documents)
    : MindmapCommandHandler(documents), ICommandHandler<RemoveNodeCommand>
{
    public async Task<CommandResult> ExecuteAsync(RemoveNodeCommand command, CancellationToken cancellationToken = default)
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

        if (node.IsRoot)
        {
            return CommandResult.Failure("The root node cannot be removed.");
        }

        var parent = node.Parent!;
        var index = node.IndexInParent;
        var removedIds = node.Element.DescendantsAndSelf(MindmapNode.ElementName)
            .Select(element => element.Attribute(MindmapNode.IdAttribute)?.Value ?? "")
            .ToArray();

        var subtree = document.Remove(node);
        var saved = await Documents.SaveAsync(command.BodyPath, document, new MindmapStructureChanged(removedIds), cancellationToken);

        return CommandResult.FromSave(
            saved, new RestoreSubtreeCommand(command.BodyPath, parent.Id, index, FreeplaneXmlWriter.ToText(subtree)));
    }
}

internal sealed class RestoreSubtreeCommandHandler(IMindmapDocumentStore documents)
    : MindmapCommandHandler(documents), ICommandHandler<RestoreSubtreeCommand>
{
    public async Task<CommandResult> ExecuteAsync(RestoreSubtreeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var resolution = await ResolveAsync(command.BodyPath, command.ParentId, cancellationToken);
        if (resolution.Failure is { } failure)
        {
            return failure;
        }

        var document = resolution.Document;
        var parent = resolution.Node;

        var subtree = MindmapDocument.ParseFragment(command.Subtree);
        var restoredId = subtree.Attribute(MindmapNode.IdAttribute)?.Value ?? "";
        if (restoredId.Length > 0 && document.Find(restoredId) is not null)
        {
            // A redo of the add that created this branch beat us to it, or the user recreated
            // the id by hand; either way putting a second copy in would corrupt the map.
            return CommandResult.Failure("A node with that id already exists.");
        }

        document.Restore(subtree, parent, command.Index);
        var saved = await Documents.SaveAsync(command.BodyPath, document, MindmapStructureChanged.Nothing, cancellationToken);

        return CommandResult.FromSave(saved, new RemoveNodeCommand(command.BodyPath, restoredId));
    }
}
