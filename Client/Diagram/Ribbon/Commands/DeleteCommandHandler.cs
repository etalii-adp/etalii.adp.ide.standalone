using Blazor.Diagrams.Core.Anchors;

namespace EtAlii.Adp.Client;
using Cn = CommandName;

public class DeleteCommandHandler : CommandHandler<DeleteCommand>
{
    public override string CommandName => Cn.Delete;

    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    public static Command[] CreateCommands(DiagramContext context)
    {
        var linkCommands = context.Selection
            .OfType<LinkView>()
            .Select(l => CreateRemoveLinkCommand(l, context))
            .ToArray();

        var nodeCommands = context.Selection
            .OfType<NodeView>()
            .SelectMany(n => CreateRemoveNodeCommands(n, context))
            .ToArray();
        
        var commands = linkCommands
            .Concat(nodeCommands)
            .Concat([new DeleteCommand()])
            .OrderBy(c => c is NodeRemoveCommand) // We first will remove the nodes.
            .ToArray();
        
        return commands;
    }
    
    protected override Task Do(DeleteCommand command, DiagramContext context)
    {
        context.View.UnselectAll(); // We want to unselect everything as the nodes and links will be deleted.

        return Task.CompletedTask;
    }

    protected override Task Undo(DeleteCommand command, DiagramContext context)
    {
        return Task.CompletedTask;
    }
    
    private static Command[] CreateRemoveNodeCommands(NodeView node, DiagramContext context)
    {
        var commands = node.Ports
            .SelectMany(p => p.Links)
            .ToArray() // Needed to safeguard against collection modifications.
            .OfType<LinkView>()
            .Select(l => CreateRemoveLinkCommand(l, context))
            .ToList();

        var command = RemoveNodeCommandHandler.Create(context, node.Id, node.Position, node.Name);
        commands.Add(command);
        
        return commands.ToArray();
    }

    private static Command CreateRemoveLinkCommand(LinkView link, DiagramContext context)
    {
        var source = (PortView)((SinglePortAnchor)link.Source).Port;
        var target = (PortView)((SinglePortAnchor)link.Target).Port;

        var command = RemoveLinkCommandHandler.CreateCommand(context, link.Id, source.NodeIdentifier, source.Id, target.NodeIdentifier, target.Id);
        
        return command;
    }
}