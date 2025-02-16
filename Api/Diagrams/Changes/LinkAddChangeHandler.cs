using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class LinkAddChangeHandler : ChangeHandler<LinkAddChange>
{
    protected override async Task Apply(LinkAddChange change, AdpDbContext context)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(d => d.Id == change.DiagramId);
        var startNode = await context.Nodes.SingleAsync(n => n.Id == change.StartNodeId);
        var endNode = await context.Nodes.SingleAsync(n => n.Id == change.EndNodeId);
        
        // Apply changes.
        var link = new Link
        {
            Diagram = diagram,
            Id = change.NewLinkId,
            StartNode = startNode,
            StartPort = change.StartPort,
            EndNode = endNode,
            EndPort = change.EndPort,
        };
        diagram.Links.Add(link);
        
        // Tag for modification and addition.
        context.Entry(diagram).State = EntityState.Modified;
        context.Entry(startNode).State = EntityState.Modified;
        context.Entry(endNode).State = EntityState.Modified;
        context.Entry(link).State = EntityState.Added;
    }
    
    protected override async Task Undo(LinkAddChange change, AdpDbContext context)
    {
        // Fetch the diagram.
        var link = await context.Links.SingleAsync(l => l.Id == change.NewLinkId);
        
        // Tag for deletion.
        context.Entry(link).State = EntityState.Deleted;
    }
}