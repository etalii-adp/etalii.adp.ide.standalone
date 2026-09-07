

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>Add a node immediately after <paramref name="SiblingId"/> under the same parent (Requirement 7.2).</summary>
public sealed record AddSiblingNodeCommand(string BodyPath, string SiblingId, string NodeId, string Text) : ICommand;

internal sealed class AddSiblingNodeCommandHandler(IMindmapDocumentStore documents)
    : MindmapCommandHandler(documents), ICommandHandler<AddSiblingNodeCommand>
{
    public Task<CommandResult> ExecuteAsync(AddSiblingNodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolve(command.BodyPath, command.SiblingId, out var document, out var sibling, out var failure))
        {
            return Task.FromResult(failure);
        }

        if (sibling.IsRoot)
        {
            return Task.FromResult(CommandResult.Failure("The root node cannot have a sibling."));
        }

        if (document.Find(command.NodeId) is not null)
        {
            return Task.FromResult(CommandResult.Failure("A node with that id already exists."));
        }

        var added = document.AddSibling(sibling, command.Text);
        added.SetId(command.NodeId);
        Documents.Save(command.BodyPath, MindmapStructureChanged.Nothing);

        return Task.FromResult(CommandResult.Success(new RemoveNodeCommand(command.BodyPath, command.NodeId)));
    }
}
