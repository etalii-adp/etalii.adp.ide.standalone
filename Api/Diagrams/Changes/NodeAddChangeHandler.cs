using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class NodeAddChangeHandler : ChangeHandler<NodeAddChange>
{
    protected override async Task Apply(NodeAddChange change, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(d => d.Id == change.DiagramId);

        // Apply changes.
        var node = new Node
        {
            Diagram = diagram,
            Id = change.NewNodeId,
            Position = change.NewPosition
        };
        diagram.Nodes.Add(node);
        
        // Tag for modification and addition.
        context.Entry(diagram).State = EntityState.Modified;
        context.Entry(node).State = EntityState.Added;
    }
    
    protected override async Task Undo(NodeAddChange change, AdpDbContext context)
    {
        // Fetch the diagram.
        var node = await context.Nodes.SingleAsync(n => n.Id == change.NewNodeId);
        
        // Tag for deletion.
        context.Entry(node).State = EntityState.Deleted;
    }
}