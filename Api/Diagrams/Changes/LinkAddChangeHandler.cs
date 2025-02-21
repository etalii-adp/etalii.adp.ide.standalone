using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public class LinkAddChangeHandler : ChangeHandler<LinkAddChange>
{
    protected override async Task Do(LinkAddChange change, AdpDbContext context)
    {
        await Apply(context, change.DiagramId, change.NewLinkId, change.SourceNodeId, change.SourcePort, change.TargetNodeId, change.TargetPort);
    }

    public async Task Apply(
        AdpDbContext context, 
        DiagramIdentifier diagramId,
        LinkIdentifier linkId, 
        NodeIdentifier sourceNodeId,
        string sourcePort,
        NodeIdentifier targetNodeId,
        string targetPort)
    {
        // Fetch the diagram.
        var diagram = await context.Diagrams.SingleAsync(d => d.Id == diagramId);
        var sourceNode = await context.Nodes.SingleAsync(n => n.Id == sourceNodeId);
        var targetNode = await context.Nodes.SingleAsync(n => n.Id == targetNodeId);
        
        // Apply changes.
        var link = new Link
        {
            Diagram = diagram,
            Id = linkId,
            SourceNode = sourceNode,
            SourcePort = sourcePort,
            TargetNode = targetNode,
            TargetPort = targetPort,
        };
        diagram.Links.Add(link);
        
        // Tag for modification and addition.
        context.Entry(diagram).State = EntityState.Modified;
        context.Entry(sourceNode).State = EntityState.Modified;
        context.Entry(targetNode).State = EntityState.Modified;
        context.Entry(link).State = EntityState.Added;
        
    }
    
    protected override async Task Undo(LinkAddChange change, AdpDbContext context)
    {
        await Undo(context, change.NewLinkId);
    }

    public async Task Undo(AdpDbContext context, LinkIdentifier linkId)
    {
        // Fetch the diagram.
        var link = await context.Links.SingleAsync(l => l.Id == linkId);
        
        // Tag for deletion.
        context.Entry(link).State = EntityState.Deleted;
        
        
        // // Fetch the diagram.
        // var diagram = await context.Diagrams.SingleAsync(d => d.Id == change.DiagramId);
        // var sourceNode = await context.Nodes.SingleAsync(n => n.Id == change.SourceNodeId);
        // var targetNode = await context.Nodes.SingleAsync(n => n.Id == change.TargetNodeId);
        // var link = await context.Links.SingleAsync(l => l.Id == change.OldLinkId);        
        //
        // // Apply changes.
        // diagram.Links.Remove(link);
        //
        // // Tag for modification and addition.
        // context.Entry(diagram).State = EntityState.Modified;
        // context.Entry(sourceNode).State = EntityState.Modified;
        // context.Entry(targetNode).State = EntityState.Modified;
        // context.Entry(link).State = EntityState.Deleted;
    }
}