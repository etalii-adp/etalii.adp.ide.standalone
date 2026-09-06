using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// Move <paramref name="NodeId"/> and its subtree to be child number <paramref name="Index"/>
/// of <paramref name="NewParentId"/> (Requirement 7.3).
/// </summary>
public sealed record MoveNodeCommand(string BodyPath, string NodeId, string NewParentId, int Index) : ICommand;

internal sealed class MoveNodeCommandHandler(IMindmapDocumentStore documents)
    : MindmapCommandHandler(documents), ICommandHandler<MoveNodeCommand>
{
    public Task<CommandResult> ExecuteAsync(MoveNodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolve(command.BodyPath, command.NodeId, out var document, out var node, out var failure))
        {
            return Task.FromResult(failure);
        }

        if (node.IsRoot)
        {
            return Task.FromResult(CommandResult.Failure("The root node cannot be moved."));
        }

        var newParent = document.Find(command.NewParentId);
        if (newParent is null)
        {
            return Task.FromResult(CommandResult.Failure("The destination node no longer exists."));
        }

        if (MindmapDocument.IsWithin(newParent, node))
        {
            return Task.FromResult(CommandResult.Failure("A node cannot be moved into its own branch."));
        }

        // Captured before the move: the inverse restores the previous parent and the previous
        // place among its siblings, not merely the parent (Requirement 7.3).
        var previousParent = node.Parent!;
        var previousIndex = node.IndexInParent;

        document.Move(node, newParent, command.Index);
        Documents.Save(command.BodyPath, MindmapStructureChanged.Nothing);

        return Task.FromResult(CommandResult.Success(new MoveNodeCommand(command.BodyPath, command.NodeId, previousParent.Id, previousIndex)));
    }
}
