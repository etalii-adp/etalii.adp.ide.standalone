using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class NodeMoveChangeHandler : ChangeHandler<NodeMoveChange>
{
    protected override async Task Apply(NodeMoveChange change, AdpDbContext context)
    {
        // Fetch the node.
        var node = await context.Nodes.SingleAsync(n => n.Id == change.NodeId);
        
        node.Position = change.NewPosition;
        
        // Tag for modification.
        context.Entry(node).State = EntityState.Modified;
    }
    
    protected override async Task Undo(NodeMoveChange change, AdpDbContext context)
    {
        // Fetch the node.
        var node = await context.Nodes.SingleAsync(n => n.Id == change.NodeId);
        
        node.Position = change.OldPosition;
        
        // Tag for modification.
        context.Entry(node).State = EntityState.Modified;
    }
}