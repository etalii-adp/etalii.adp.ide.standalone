using EtAlii.Adp.History;

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
    public async Task<CommandResult> ExecuteAsync(AddChildNodeCommand command, CancellationToken cancellationToken = default)
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

        if (document.Find(command.NodeId) is not null)
        {
            return CommandResult.Failure("A node with that id already exists.");
        }

        var added = document.AddChild(parent, command.Text);
        added.SetId(command.NodeId);
        var saved = await Documents.SaveAsync(command.BodyPath, document, MindmapStructureChanged.Nothing, cancellationToken);

        return CommandResult.FromSave(saved, new RemoveNodeCommand(command.BodyPath, command.NodeId));
    }
}
