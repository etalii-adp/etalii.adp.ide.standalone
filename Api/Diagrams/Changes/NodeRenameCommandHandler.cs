using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class NodeRenameCommandHandler : CommandHandler<NodeRenameCommand>
{
    protected override async Task Do(NodeRenameCommand command, AdpDbContext context)
    {
        // Fetch the node.
        var node = await context.Nodes.SingleAsync(n => n.Id == command.NodeId);
        
        node.Name = command.NewName;
        
        // Tag for modification.
        context.Entry(node).State = EntityState.Modified;
    }
    
    protected override async Task Undo(NodeRenameCommand command, AdpDbContext context)
    {
        // Fetch the node.
        var node = await context.Nodes.SingleAsync(n => n.Id == command.NodeId);
        
        node.Name = command.OldName;
        
        // Tag for modification.
        context.Entry(node).State = EntityState.Modified;
    }
}