using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class NodeMoveCommandHandler : CommandHandler<NodeMoveCommand>
{
    protected override async Task Do(NodeMoveCommand command, AdpDbContext context)
    {
        // Fetch the node.
        var node = await context.Nodes.SingleAsync(n => n.Id == command.NodeId);
        
        node.Position = command.NewPosition;
        
        // Tag for modification.
        context.Entry(node).State = EntityState.Modified;
    }
    
    protected override async Task Undo(NodeMoveCommand command, AdpDbContext context)
    {
        // Fetch the node.
        var node = await context.Nodes.SingleAsync(n => n.Id == command.NodeId);
        
        node.Position = command.OldPosition;
        
        // Tag for modification.
        context.Entry(node).State = EntityState.Modified;
    }
}