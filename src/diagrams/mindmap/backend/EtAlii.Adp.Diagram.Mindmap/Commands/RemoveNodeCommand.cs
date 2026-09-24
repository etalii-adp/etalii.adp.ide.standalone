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
    public Task<CommandResult> ExecuteAsync(RemoveNodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolve(command.BodyPath, command.NodeId, out var document, out var node, out var failure))
        {
            return Task.FromResult(failure);
        }

        if (node.IsRoot)
        {
            return Task.FromResult(CommandResult.Failure("The root node cannot be removed."));
        }

        var parent = node.Parent!;
        var index = node.IndexInParent;
        var removedIds = node.Element.DescendantsAndSelf(MindmapNode.ElementName)
            .Select(element => element.Attribute(MindmapNode.IdAttribute)?.Value ?? "")
            .ToArray();

        var subtree = document.Remove(node);
        var saved = Documents.Save(command.BodyPath, document, new MindmapStructureChanged(removedIds));

        return Task.FromResult(saved.Failed
            ? CommandResult.Failure(saved.Error)
            : CommandResult.Success(
                new RestoreSubtreeCommand(command.BodyPath, parent.Id, index, FreeplaneXmlWriter.ToText(subtree))));
    }
}

internal sealed class RestoreSubtreeCommandHandler(IMindmapDocumentStore documents)
    : MindmapCommandHandler(documents), ICommandHandler<RestoreSubtreeCommand>
{
    public Task<CommandResult> ExecuteAsync(RestoreSubtreeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolve(command.BodyPath, command.ParentId, out var document, out var parent, out var failure))
        {
            return Task.FromResult(failure);
        }

        var subtree = MindmapDocument.ParseFragment(command.Subtree);
        var restoredId = subtree.Attribute(MindmapNode.IdAttribute)?.Value ?? "";
        if (restoredId.Length > 0 && document.Find(restoredId) is not null)
        {
            // A redo of the add that created this branch beat us to it, or the user recreated
            // the id by hand; either way putting a second copy in would corrupt the map.
            return Task.FromResult(CommandResult.Failure("A node with that id already exists."));
        }

        document.Restore(subtree, parent, command.Index);
        var saved = Documents.Save(command.BodyPath, document, MindmapStructureChanged.Nothing);

        return Task.FromResult(saved.Failed
            ? CommandResult.Failure(saved.Error)
            : CommandResult.Success(new RemoveNodeCommand(command.BodyPath, restoredId)));
    }
}
