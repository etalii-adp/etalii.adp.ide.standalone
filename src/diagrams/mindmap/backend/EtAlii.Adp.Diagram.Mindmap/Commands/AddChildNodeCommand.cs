using EtAlii.Adp.Backend;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>Add a node as the last child of <paramref name="ParentId"/> (Requirement 7.1).</summary>
/// <param name="NodeId">
/// The id the new node gets. Chosen by the caller rather than the handler so a redo recreates
/// the same node, and so the inverse can name it before it exists.
/// </param>
public sealed record AddChildNodeCommand(string BodyPath, string ParentId, string NodeId, string Text) : ICommand;

internal sealed class AddChildNodeCommandHandler(IMindmapDocumentStore documents)
    : MindmapCommandHandler(documents), ICommandHandler<AddChildNodeCommand>
{
    public Task<CommandResult> ExecuteAsync(AddChildNodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolve(command.BodyPath, command.ParentId, out var document, out var parent, out var failure))
        {
            return Task.FromResult(failure);
        }

        if (document.Find(command.NodeId) is not null)
        {
            return Task.FromResult(CommandResult.Failure("A node with that id already exists."));
        }

        var added = document.AddChild(parent, command.Text);
        added.SetId(command.NodeId);
        Documents.Save(command.BodyPath, MindmapStructureChanged.Nothing);

        return Task.FromResult(CommandResult.Success(new RemoveNodeCommand(command.BodyPath, command.NodeId)));
    }
}
