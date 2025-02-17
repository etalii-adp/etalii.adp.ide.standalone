using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class NodeRenameChangeHandler : ChangeHandler<NodeRenameChange>
{
    protected override async Task Apply(NodeRenameChange change, AdpDbContext context)
    {
        // Fetch the node.
        var node = await context.Nodes.SingleAsync(n => n.Id == change.NodeId);
        
        node.Name = change.NewName;
        
        // Tag for modification.
        context.Entry(node).State = EntityState.Modified;
    }
    
    protected override async Task Undo(NodeRenameChange change, AdpDbContext context)
    {
        // Fetch the node.
        var node = await context.Nodes.SingleAsync(n => n.Id == change.NodeId);
        
        node.Name = change.OldName;
        
        // Tag for modification.
        context.Entry(node).State = EntityState.Modified;
    }
}